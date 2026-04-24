// ============================================================
// Controllers/FeesController.cs
//
// Endpoints:
//   GET  /api/fees/dues/{studentId}   fee ledger for a student
//   POST /api/fees/pay                record payment + receipt number
//   GET  /api/fees/receipt/{id}       stream PDF receipt
//   GET  /api/fees/summary            monthly/yearly collected vs pending
// ============================================================

using Azure;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolPanel.Controllers.DTOs;
using SchoolPanel.Controllers.Services;
using System.Data;
using System.Data.SqlClient;

namespace SchoolPanel.Controllers.Controllers;

[ApiController]
[Route("api/legacy/fees")]
[Authorize]
[EnableRateLimiting("PerUser")]
public sealed class FeesController : AppControllerBase
{
    private readonly IConfiguration _config;
    private readonly IPdfReceiptService _pdf;
    private readonly IBlobStorageService _blob;
    private readonly ILogger<FeesController> _log;

    public FeesController(
        IConfiguration config,
        IPdfReceiptService pdf,
        IBlobStorageService blob,
        ILogger<FeesController> log)
    {
        _config = config;
        _pdf = pdf;
        _blob = blob;
        _log = log;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/fees/dues/{studentId}
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Full fee ledger for a student.
    ///
    /// Returns:
    ///   - Per fee-type: amount due, paid, discount, fine, balance, due date
    ///   - Grand totals
    ///   - IsOverdue flag per line (DueDate < today AND balance > 0)
    ///   - Payment history
    /// </summary>
    [HttpGet("dues/{studentId:guid}")]
    [Authorize(Policy = "Fees.View")]
    [ProducesResponseType(typeof(FeesDue), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetDues(
        Guid studentId,
        [FromQuery] int? academicYearId,
        CancellationToken ct)
    {
        using var conn = Db();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetStudentFeeStatus",
            new { StudentId = studentId, AcademicYearId = academicYearId },
            commandType: CommandType.StoredProcedure);

        // Result set 1: fee type rows
        var feeTypeRows = (await multi.ReadAsync<dynamic>()).ToList();

        // Result set 2: payment history rows
        var paymentRows = (await multi.ReadAsync<dynamic>()).ToList();

        if (feeTypeRows.Count == 0)
            return NotFoundProblem($"No fee data found for student '{studentId}'.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var lineItems = feeTypeRows.Select(r =>
        {
            DateOnly? dueDate = r.DueDate is null ? null
                : DateOnly.FromDateTime((DateTime)r.DueDate);

            decimal balance = (decimal)(r.BalanceDue ?? 0m);
            bool isOverdue = dueDate.HasValue
                              && dueDate.Value < today
                              && balance > 0;

            return new FeeLineItem(
                FeeTypeId: (int)r.FeeTypeId,
                FeeTypeName: (string)r.FeeTypeName,
                Frequency: (string)r.Frequency,
                AmountDue: (decimal)(r.FeeAmount ?? 0m),
                AmountPaid: (decimal)(r.TotalPaid ?? 0m),
                Discount: (decimal)(r.TotalDiscount ?? 0m),
                Fine: (decimal)(r.TotalFine ?? 0m),
                Balance: balance,
                DueDate: dueDate,
                IsOverdue: isOverdue);
        }).ToList();

        // Grand totals
        var grandDue = lineItems.Sum(l => l.AmountDue);
        var grandPaid = lineItems.Sum(l => l.AmountPaid);
        var grandBalance = lineItems.Sum(l => l.Balance);

        // Student name lookup from first fee row (SP joins it)
        var firstRow = feeTypeRows.First();

        return Ok(new FeesDue(
            StudentId: studentId,
            StudentName: (string)(firstRow.FullName ?? string.Empty),
            RollNumber: (string)(firstRow.RollNumber ?? string.Empty),
            ClassName: (string)(firstRow.ClassName ?? string.Empty),
            LineItems: lineItems.AsReadOnly(),
            GrandTotalDue: grandDue,
            GrandTotalPaid: grandPaid,
            GrandBalance: grandBalance
        ));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/fees/pay
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Record a fee payment.
    ///
    /// Receipt number is auto-generated server-side:
    ///   Format: RCP-YYYYMMDD-NNNNNN (e.g. RCP-20241115-000042)
    ///
    /// The generated PDF receipt is uploaded to Azure Blob and its URL
    /// is returned in the response for immediate download.
    ///
    /// Returns: { paymentId, receiptNumber, receiptUrl }
    /// </summary>
    [HttpPost("pay")]
    [Authorize(Policy = "Fees.Create")]
    [ProducesResponseType(typeof(object), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> RecordPayment(
        [FromBody] RecordPaymentRequest request,
        CancellationToken ct)
    {
        if (request.AmountPaid > request.AmountDue + request.Fine)
            return BadRequestProblem(
                $"AmountPaid ({request.AmountPaid:N2}) cannot exceed " +
                $"AmountDue + Fine ({request.AmountDue + request.Fine:N2}).");

        var collectedById = CurrentUserId!.Value;

        using var conn = Db();

        // ── 1. Persist the payment ─────────────────────────────────────────────
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_RecordFeePayment",
            new
            {
                request.StudentId,
                request.FeeTypeId,
                request.AcademicYearId,
                request.AmountDue,
                request.AmountPaid,
                request.Discount,
                request.Fine,
                request.PaymentMethod,
                request.ReferenceNumber,
                request.Remarks,
                CollectedById = collectedById
            },
            commandType: CommandType.StoredProcedure);

        string code = result?.ResultCode ?? "ERROR";
        long? paymentId = result?.PaymentId;
        string? receiptNumber = result?.ReceiptNumber;

        if (code != "SUCCESS" || paymentId == null)
            return ServerErrorProblem("Payment could not be recorded. Please retry.");

        // ── 2. Build receipt DTO ───────────────────────────────────────────────
        var receiptDto = await BuildReceiptDtoAsync(conn, paymentId.Value, ct);

        // ── 3. Generate PDF ────────────────────────────────────────────────────
        string? receiptUrl = null;
        try
        {
            var pdfBytes = _pdf.GenerateReceipt(receiptDto);
            var pdfName = $"{receiptNumber}.pdf";

            var uploadResult = await _blob.UploadBytesAsync(
                pdfBytes, "application/pdf",
                BlobFolder.Receipts, pdfName, ct);

            if (uploadResult.Success)
                receiptUrl = uploadResult.Url;
            else
                _log.LogWarning(
                    "PDF upload failed for {Rcp}: {Err}",
                    receiptNumber, uploadResult.Error);
        }
        catch (Exception ex)
        {
            // PDF generation failure must NOT roll back the payment
            _log.LogError(ex,
                "PDF generation failed for receipt {Rcp}", receiptNumber);
        }

        _log.LogInformation(
            "Fee payment recorded. Receipt={R} Student={S} Amount={A}",
            receiptNumber, request.StudentId, request.AmountPaid);

        return StatusCode(201, new
        {
            paymentId,
            receiptNumber,
            receiptUrl,
            message = "Payment recorded successfully."
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/fees/receipt/{paymentId}
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Stream the PDF receipt for a payment.
    ///
    /// Strategy:
    ///   1. Try to serve the pre-generated PDF from Azure Blob.
    ///   2. If not found (e.g. upload failed), regenerate on-the-fly.
    ///
    /// Content-Disposition: inline — browser renders it in a PDF viewer.
    /// Add ?download=true to force a file download.
    /// </summary>
    [HttpGet("receipt/{paymentId:long}")]
    [Authorize(Policy = "Fees.View")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetReceipt(
        long paymentId,
        [FromQuery] bool download = false,
        CancellationToken ct = default)
    {
        using var conn = Db();
        var receiptDto = await BuildReceiptDtoAsync(conn, paymentId, ct);

        // paymentId not found in DB
        if (string.IsNullOrEmpty(receiptDto.ReceiptNumber))
            return NotFoundProblem($"Payment '{paymentId}' not found.");

        // ── Try Azure Blob first ───────────────────────────────────────────────
        var blobName = $"fees/receipts/{receiptDto.ReceiptNumber}.pdf";
        var stream = await _blob.OpenReadAsync(blobName, ct);

        byte[]? pdfBytes = null;
        if (stream == null)
        {
            // Regenerate on-the-fly
            _log.LogWarning(
                "Receipt PDF not in blob — regenerating. PaymentId={Id}", paymentId);
            pdfBytes = _pdf.GenerateReceipt(receiptDto);
        }

        var disposition = download
            ? $"attachment; filename=\"{receiptDto.ReceiptNumber}.pdf\""
            : $"inline; filename=\"{receiptDto.ReceiptNumber}.pdf\"";

        Response.Headers.Append("Content-Disposition", disposition);

        if (stream != null)
            return File(stream, "application/pdf");

        return File(pdfBytes!, "application/pdf");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/fees/summary
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Monthly and class-wise fee collection summary.
    ///
    /// Used by:
    ///   - Dashboard ApexCharts bar/line chart (monthly trend)
    ///   - Fee reports page (class-wise table)
    ///   - Export to Excel
    ///
    /// Optional filters: classId, month, year
    /// </summary>
    [HttpGet("summary")]
    [Authorize(Policy = "Fees.View")]
    [ResponseCache(Duration = 180, VaryByQueryKeys = ["academicYearId", "classId", "month", "year"])]
    [ProducesResponseType(typeof(FeeCollectionSummary), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] FeesSummaryQuery q,
        CancellationToken ct)
    {
        if (q.Month.HasValue && (q.Month < 1 || q.Month > 12))
            return BadRequestProblem("Month must be between 1 and 12.");

        using var conn = Db();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetFeeCollectionSummary",
            new
            {
                AcademicYearId = q.AcademicYearId,
                ClassId = q.ClassId,
                Month = q.Month,
                Year = q.Year
            },
            commandType: CommandType.StoredProcedure);

        // Result set 1: monthly breakdown
        var monthly = (await multi.ReadAsync<MonthlyFee>())
                       .ToList().AsReadOnly();

        // Result set 2: class-wise breakdown
        var byClass = (await multi.ReadAsync<ClassFee>())
                       .ToList().AsReadOnly();

        // Grand totals from the monthly data
        var grandDue = monthly.Sum(m => m.TotalDue);
        var grandCollected = monthly.Sum(m => m.TotalCollected);
        var grandPending = monthly.Sum(m => m.TotalPending);

        return Ok(new FeeCollectionSummary(
            Monthly: monthly,
            ByClass: byClass,
            TotalDue: grandDue,
            TotalCollected: grandCollected,
            TotalPending: grandPending
        ));
    }

    // ─── Private helpers ──────────────────────────────────────

    /// <summary>
    /// Load all data needed for receipt PDF from the DB.
    /// Uses a direct Dapper query with joins — no SP needed here.
    /// </summary>
    private async Task<PaymentReceipt> BuildReceiptDtoAsync(
        SqlConnection conn, long paymentId, CancellationToken ct)
    {
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            """
            SELECT
                fp.PaymentId,
                fp.ReceiptNumber,
                fp.AmountDue,
                fp.AmountPaid,
                fp.Discount,
                fp.Fine,
                fp.BalanceDue,
                fp.PaymentMethod,
                fp.ReferenceNumber,
                CAST(fp.PaymentDate AS DATE) AS PaymentDate,
                u_student.FullName            AS StudentName,
                s.RollNumber,
                c.ClassName                   + ' - ' + c.Section AS ClassName,
                ft.FeeTypeName,
                u_collector.FullName          AS CollectedBy,
                sch_name.SettingValue         AS SchoolName,
                sch_addr.SettingValue         AS SchoolAddress,
                sch_phone.SettingValue        AS SchoolPhone
            FROM   dbo.FeePayments fp
            JOIN   dbo.Students     s            ON fp.StudentId    = s.StudentId
            JOIN   dbo.Users        u_student    ON s.UserId        = u_student.UserId
            JOIN   dbo.Classes      c            ON s.ClassId       = c.ClassId
            JOIN   dbo.FeeTypes     ft           ON fp.FeeTypeId    = ft.FeeTypeId
            JOIN   dbo.Users        u_collector  ON fp.CollectedById = u_collector.UserId
            OUTER APPLY (
                SELECT TOP 1 SettingValue FROM dbo.Settings
                WHERE SettingKey = 'School.Name') sch_name
            OUTER APPLY (
                SELECT TOP 1 SettingValue FROM dbo.Settings
                WHERE SettingKey = 'School.Address') sch_addr
            OUTER APPLY (
                SELECT TOP 1 SettingValue FROM dbo.Settings
                WHERE SettingKey = 'School.Phone') sch_phone
            WHERE  fp.PaymentId = @PaymentId
            """,
            new { PaymentId = paymentId });

        if (row == null)
        {
            // Return a sentinel — caller checks ReceiptNumber
            return new PaymentReceipt(
                paymentId, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, 0, 0, 0, 0, 0,
                string.Empty, null, DateOnly.MinValue,
                string.Empty, string.Empty, null, null);
        }

        return new PaymentReceipt(
            PaymentId: (long)row.PaymentId,
            ReceiptNumber: (string)row.ReceiptNumber,
            StudentName: (string)row.StudentName,
            RollNumber: (string)row.RollNumber,
            ClassName: (string)row.ClassName,
            FeeTypeName: (string)row.FeeTypeName,
            AmountDue: (decimal)row.AmountDue,
            AmountPaid: (decimal)row.AmountPaid,
            Discount: (decimal)row.Discount,
            Fine: (decimal)row.Fine,
            Balance: (decimal)row.BalanceDue,
            PaymentMethod: (string)row.PaymentMethod,
            ReferenceNumber: (string?)row.ReferenceNumber,
            PaymentDate: DateOnly.FromDateTime((DateTime)row.PaymentDate),
            CollectedBy: (string)row.CollectedBy,
            SchoolName: (string)(row.SchoolName ?? "School Management Panel"),
            SchoolAddress: (string?)row.SchoolAddress,
            SchoolPhone: (string?)row.SchoolPhone
        );
    }

    private SqlConnection Db()
        => new(_config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing."));
}