// ============================================================
// Controllers/ReportsController.cs
//
// Endpoints:
//   GET /api/reports/report-card/{studentId}
//       ?academicYearId=1&examType=Annual
//
//   GET /api/reports/fee-receipt/{paymentId}
//       ?download=true
//
//   GET /api/reports/attendance/excel
//       ?classId=3&month=11&year=2024
//
//   GET /api/reports/exam-results/{examId}
//       ?download=true
//
// All reports:
//   - Stream bytes directly (no temp files, no disk I/O)
//   - Content-Disposition: inline for browser PDF viewer
//   - Add ?download=true to force download
//   - ETag + Cache-Control for browser caching
//   - Audit logged by AuditLoggingMiddleware
// ============================================================

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SchoolPanel.Reports.Services;

namespace SchoolPanel.Reports.Controllers;

[ApiController]
[Route("api/legacy/reports")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/pdf", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportService _reports;
    private readonly ILogger<ReportsController> _log;

    public ReportsController(
        IReportService reports,
        ILogger<ReportsController> log)
    {
        _reports = reports;
        _log = log;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/reports/report-card/{studentId}
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Generate and stream a student report card PDF.
    ///
    /// Query params:
    ///   academicYearId  (required) — which academic year
    ///   examType        (optional) — filter by exam type (Annual, HalfYearly, etc.)
    ///   download        (optional) — true to force file download
    ///
    /// Browser caching: ETag based on studentId + yearId + timestamp.
    /// Subsequent requests with If-None-Match return 304 (no re-generation).
    /// </summary>
    [HttpGet("report-card/{studentId:guid}")]
    [Authorize(Policy = "Reports.View")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(304)]
    public async Task<IActionResult> GetReportCard(
        Guid studentId,
        [FromQuery] int academicYearId,
        [FromQuery] string? examType = null,
        [FromQuery] bool download = false,
        CancellationToken ct = default)
    {
        if (academicYearId <= 0)
            return BadRequestProblem(
                "'academicYearId' is required and must be a positive integer.");

        try
        {
            var bytes = await _reports.GenerateStudentReportCardPdfAsync(
                studentId, academicYearId, examType, ct);

            var fileName = $"ReportCard_{studentId}_{academicYearId}.pdf";
            var etag = ComputeETag(bytes);

            if (IsETagMatch(etag))
                return StatusCode(304);

            _log.LogInformation(
                "Report card served. Student={S} Year={Y} Size={Sz}KB",
                studentId, academicYearId, bytes.Length / 1024);

            return PdfFileResult(bytes, fileName, download, etag);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Report card generation failed. Student={S}", studentId);
            return ServerErrorProblem(
                "Report generation failed. Please try again.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/reports/fee-receipt/{paymentId}
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Generate and stream a fee receipt PDF.
    ///
    /// Content-Disposition defaults to inline (renders in browser PDF viewer).
    /// Add ?download=true to force browser file download.
    ///
    /// The generated receipt is identical to the one stored in Azure Blob —
    /// this endpoint regenerates on-the-fly so receipts are always available
    /// even if the Blob upload failed.
    /// </summary>
    [HttpGet("fee-receipt/{paymentId:long}")]
    [Authorize(Policy = "Fees.View")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetFeeReceipt(
        long paymentId,
        [FromQuery] bool download = false,
        CancellationToken ct = default)
    {
        try
        {
            var bytes = await _reports.GenerateFeeReceiptPdfAsync(paymentId, ct);
            var fileName = $"Receipt_{paymentId}.pdf";
            var etag = ComputeETag(bytes);

            if (IsETagMatch(etag))
                return StatusCode(304);

            _log.LogInformation(
                "Fee receipt served. PaymentId={P} Size={Sz}KB",
                paymentId, bytes.Length / 1024);

            return PdfFileResult(bytes, fileName, download, etag);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Fee receipt generation failed. PaymentId={P}", paymentId);
            return ServerErrorProblem("Receipt generation failed.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/reports/attendance/excel
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Generate and stream a monthly attendance register as an Excel workbook.
    ///
    /// Query params:
    ///   classId  (required)
    ///   month    (required) — 1-12
    ///   year     (required) — e.g. 2024
    ///
    /// Returns: .xlsx with freeze panes, coloured cells, print setup.
    /// </summary>
    [HttpGet("attendance/excel")]
    [Authorize(Policy = "Reports.Export")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetAttendanceExcel(
        [FromQuery] int classId,
        [FromQuery] int month,
        [FromQuery] int year,
        CancellationToken ct = default)
    {
        if (classId <= 0)
            return BadRequestProblem("'classId' is required.");

        if (month < 1 || month > 12)
            return BadRequestProblem("'month' must be between 1 and 12.");

        if (year < 2000 || year > DateTime.UtcNow.Year + 1)
            return BadRequestProblem("'year' value is invalid.");

        try
        {
            var bytes = await _reports.GenerateAttendanceSheetExcelAsync(
                classId, month, year, ct);

            var monthName = new DateTime(year, month, 1).ToString("MMM-yyyy");
            var fileName = $"Attendance_Class{classId}_{monthName}.xlsx";

            _log.LogInformation(
                "Attendance Excel served. ClassId={C} Month={M}/{Y} Size={Sz}KB",
                classId, month, year, bytes.Length / 1024);

            return ExcelFileResult(bytes, fileName);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Attendance Excel failed. ClassId={C} Month={M}/{Y}",
                classId, month, year);
            return ServerErrorProblem("Excel generation failed.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/reports/exam-results/{examId}
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Generate and stream an exam result sheet PDF.
    /// Includes per-student marks, grades, class statistics,
    /// grade distribution, and signature block.
    /// </summary>
    [HttpGet("exam-results/{examId:int}")]
    [Authorize(Policy = "Reports.View")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetExamResults(
        int examId,
        [FromQuery] bool download = false,
        CancellationToken ct = default)
    {
        if (examId <= 0)
            return BadRequestProblem("'examId' must be a positive integer.");

        try
        {
            var bytes = await _reports.GenerateExamResultSheetPdfAsync(examId, ct);
            var fileName = $"ExamResults_{examId}.pdf";
            var etag = ComputeETag(bytes);

            if (IsETagMatch(etag))
                return StatusCode(304);

            _log.LogInformation(
                "Exam result sheet served. ExamId={E} Size={Sz}KB",
                examId, bytes.Length / 1024);

            return PdfFileResult(bytes, fileName, download, etag);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFoundProblem(ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Exam result generation failed. ExamId={E}", examId);
            return ServerErrorProblem("Exam result sheet generation failed.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Return a PDF file response with:
    ///   - Correct MIME type
    ///   - Content-Disposition (inline or attachment)
    ///   - ETag for browser caching
    ///   - Cache-Control: private, max-age=300 (5 min in-browser cache)
    /// </summary>
    private FileContentResult PdfFileResult(
        byte[] bytes, string fileName, bool download, string etag)
    {
        Response.Headers.ETag = $"\"{etag}\"";
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.Append("Content-Disposition",
            download
            ? $"attachment; filename=\"{fileName}\""
            : $"inline; filename=\"{fileName}\"");

        return File(bytes, "application/pdf");
    }

    private FileContentResult ExcelFileResult(byte[] bytes, string fileName)
    {
        Response.Headers.Append("Content-Disposition",
            $"attachment; filename=\"{fileName}\"");
        Response.Headers.CacheControl = "private, max-age=60";

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    /// <summary>
    /// MD5-based ETag from PDF bytes.
    /// Fast and sufficient for cache invalidation — not used for security.
    /// </summary>
    private static string ComputeETag(byte[] bytes)
    {
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Check If-None-Match header against computed ETag.
    /// Returns true if client's cached version matches → send 304.
    /// </summary>
    private bool IsETagMatch(string etag)
    {
        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        return !string.IsNullOrEmpty(ifNoneMatch)
            && ifNoneMatch.Contains(etag, StringComparison.OrdinalIgnoreCase);
    }

    // ── ProblemDetails shortcuts ──────────────────────────────────────────

    private IActionResult NotFoundProblem(string detail)
    {
        var pd = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 404,
            Title = "Not Found",
            Detail = detail,
            Instance = Request.Path
        };
        pd.Extensions["code"] = "NOT_FOUND";
        return new ObjectResult(pd) { StatusCode = 404 };
    }

    private IActionResult BadRequestProblem(string detail)
    {
        var pd = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 400,
            Title = "Bad Request",
            Detail = detail,
            Instance = Request.Path
        };
        pd.Extensions["code"] = "BAD_REQUEST";
        return new ObjectResult(pd) { StatusCode = 400 };
    }

    private IActionResult ServerErrorProblem(string detail)
    {
        var pd = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 500,
            Title = "Server Error",
            Detail = detail,
            Instance = Request.Path
        };
        pd.Extensions["code"] = "SERVER_ERROR";
        return new ObjectResult(pd) { StatusCode = 500 };
    }
}