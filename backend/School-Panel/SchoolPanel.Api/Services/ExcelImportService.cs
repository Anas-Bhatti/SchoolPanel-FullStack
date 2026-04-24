// ============================================================
// Services/ExcelImportService.cs
// Parses a student bulk-import .xlsx file using ClosedXML.
// Returns validated rows ready for sp_CreateStudent calls.
// NuGet: ClosedXML
// ============================================================

using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SchoolPanel.Controllers.DTOs;

namespace SchoolPanel.Controllers.Services;

public sealed record StudentImportRow(
    int RowNumber,
    string Email,
    string Password,
    string FullName,
    string? PhoneNumber,
    string RollNumber,
    int ClassId,
    int AcademicYearId,
    string Gender,
    string? BloodGroup,
    DateOnly? DateOfBirth,
    string? Address,
    string? EmergencyContact,
    string? ParentEmail
);

public sealed record ParseResult(
    IReadOnlyList<StudentImportRow> ValidRows,
    IReadOnlyList<BulkRowError> Errors
);

public interface IExcelImportService
{
    Task<ParseResult> ParseStudentFileAsync(
        IFormFile file, CancellationToken ct = default);

    /// <summary>Return a filled Excel template the user can download.</summary>
    byte[] GenerateTemplate();
}

public sealed class ExcelImportService : IExcelImportService
{
    private readonly ILogger<ExcelImportService> _log;

    // Expected column headers (case-insensitive, order does not matter)
    private static readonly string[] RequiredColumns =
    [
        "Email", "Password", "FullName", "RollNumber",
        "ClassId", "AcademicYearId"
    ];

    public ExcelImportService(ILogger<ExcelImportService> log)
        => _log = log;

    public async Task<ParseResult> ParseStudentFileAsync(
        IFormFile file, CancellationToken ct = default)
    {
        // Validate extension
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls")
            return new ParseResult([], [new BulkRowError(0, string.Empty,
                "File must be an Excel workbook (.xlsx or .xls).")]);

        // Validate MIME
        if (!file.ContentType.Contains("spreadsheet") &&
            !file.ContentType.Contains("excel") &&
            !file.ContentType.Contains("openxml"))
            return new ParseResult([], [new BulkRowError(0, string.Empty,
                "Invalid file type. Please upload an Excel file.")]);

        await using var stream = file.OpenReadStream();

        try
        {
            return Parse(stream);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Excel parse error");
            return new ParseResult([], [new BulkRowError(0, string.Empty,
                $"Could not parse file: {ex.Message}")]);
        }
    }

    private static ParseResult Parse(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        if (lastRow < 2)
            return new ParseResult([], [new BulkRowError(1, string.Empty,
                "The file contains no data rows.")]);

        // ── Discover column positions by header name ──────────
        var headerRow = ws.Row(1);
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var col = 1; col <= ws.LastColumnUsed()?.ColumnNumber(); col++)
        {
            var header = headerRow.Cell(col).GetString().Trim();
            if (!string.IsNullOrEmpty(header))
                colMap[header] = col;
        }

        // Validate required headers present
        var missing = RequiredColumns.Where(r => !colMap.ContainsKey(r)).ToList();
        if (missing.Count > 0)
            return new ParseResult([], [new BulkRowError(1, string.Empty,
                $"Missing required columns: {string.Join(", ", missing)}")]);

        int C(string name) => colMap.TryGetValue(name, out var v) ? v : 0;
        string Cell(IXLRow row, string col) => C(col) > 0
            ? row.Cell(C(col)).GetString().Trim()
            : string.Empty;

        var validRows = new List<StudentImportRow>();
        var errors = new List<BulkRowError>();

        for (var rowNum = 2; rowNum <= lastRow; rowNum++)
        {
            var row = ws.Row(rowNum);
            var rollNumber = Cell(row, "RollNumber");

            // Skip genuinely empty rows
            if (string.IsNullOrWhiteSpace(rollNumber) &&
                string.IsNullOrWhiteSpace(Cell(row, "Email")))
                continue;

            var rowErrors = new List<string>();

            // ── Validate required fields ──────────────────────
            var email = Cell(row, "Email");
            var password = Cell(row, "Password");
            var fullName = Cell(row, "FullName");

            if (string.IsNullOrWhiteSpace(email))
                rowErrors.Add("Email is required.");
            else if (!email.Contains('@'))
                rowErrors.Add($"Invalid email: '{email}'.");

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                rowErrors.Add("Password must be at least 8 characters.");

            if (string.IsNullOrWhiteSpace(fullName))
                rowErrors.Add("FullName is required.");

            if (string.IsNullOrWhiteSpace(rollNumber))
                rowErrors.Add("RollNumber is required.");

            if (!int.TryParse(Cell(row, "ClassId"), out var classId) || classId <= 0)
                rowErrors.Add("ClassId must be a positive integer.");

            if (!int.TryParse(Cell(row, "AcademicYearId"), out var academicYearId)
                || academicYearId <= 0)
                rowErrors.Add("AcademicYearId must be a positive integer.");

            // ── Optional fields ───────────────────────────────
            var gender = Cell(row, "Gender");
            if (string.IsNullOrWhiteSpace(gender)) gender = "Male";

            DateOnly? dob = null;
            var dobStr = Cell(row, "DateOfBirth");
            if (!string.IsNullOrWhiteSpace(dobStr))
            {
                if (DateOnly.TryParse(dobStr, out var parsedDob))
                    dob = parsedDob;
                else
                    rowErrors.Add($"Invalid DateOfBirth: '{dobStr}'. Use YYYY-MM-DD.");
            }

            if (rowErrors.Count > 0)
            {
                errors.Add(new BulkRowError(
                    rowNum, rollNumber,
                    string.Join(" | ", rowErrors)));
                continue;
            }

            validRows.Add(new StudentImportRow(
                RowNumber: rowNum,
                Email: email,
                Password: password,
                FullName: fullName,
                PhoneNumber: NullIfEmpty(Cell(row, "PhoneNumber")),
                RollNumber: rollNumber,
                ClassId: classId,
                AcademicYearId: academicYearId,
                Gender: gender,
                BloodGroup: NullIfEmpty(Cell(row, "BloodGroup")),
                DateOfBirth: dob,
                Address: NullIfEmpty(Cell(row, "Address")),
                EmergencyContact: NullIfEmpty(Cell(row, "EmergencyContact")),
                ParentEmail: NullIfEmpty(Cell(row, "ParentEmail"))
            ));
        }

        return new ParseResult(validRows, errors);
    }

    public byte[] GenerateTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Students");

        // Header row
        var headers = new[]
        {
            "Email*", "Password*", "FullName*", "PhoneNumber",
            "RollNumber*", "ClassId*", "AcademicYearId*",
            "Gender", "BloodGroup", "DateOfBirth(YYYY-MM-DD)",
            "Address", "EmergencyContact", "ParentEmail"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2563EB");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Sample row
        ws.Cell(2, 1).Value = "ali.raza@school.edu";
        ws.Cell(2, 2).Value = "SecurePass123";
        ws.Cell(2, 3).Value = "Ali Raza";
        ws.Cell(2, 4).Value = "03001234567";
        ws.Cell(2, 5).Value = "2024-001";
        ws.Cell(2, 6).Value = 1;
        ws.Cell(2, 7).Value = 1;
        ws.Cell(2, 8).Value = "Male";
        ws.Cell(2, 9).Value = "A+";
        ws.Cell(2, 10).Value = "2010-05-15";

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string? NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}