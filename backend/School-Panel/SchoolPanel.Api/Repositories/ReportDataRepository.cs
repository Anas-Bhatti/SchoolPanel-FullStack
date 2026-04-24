// ============================================================
// Services/ReportDataRepository.cs
// All database access for report generation.
// Uses Dapper multi-result (QueryMultipleAsync) to load
// every report's data in a single round-trip per report.
// ============================================================

using System.Data;
using System.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolPanel.Reports.Models;

namespace SchoolPanel.Reports.Services;

public interface IReportDataRepository
{
    Task<ReportCardData> GetReportCardDataAsync(
        Guid studentId, int academicYearId, CancellationToken ct = default);

    Task<FeeReceiptData> GetFeeReceiptDataAsync(
        long paymentId, CancellationToken ct = default);

    Task<AttendanceSheetData> GetAttendanceSheetDataAsync(
        int classId, int month, int year, CancellationToken ct = default);

    Task<ExamResultSheetData> GetExamResultSheetDataAsync(
        int examId, CancellationToken ct = default);

    Task<SchoolSettings> GetSchoolSettingsAsync(
        CancellationToken ct = default);
}

public sealed class ReportDataRepository : IReportDataRepository
{
    private readonly IConfiguration _config;
    private readonly ILogger<ReportDataRepository> _log;

    public ReportDataRepository(
        IConfiguration config,
        ILogger<ReportDataRepository> log)
    {
        _config = config;
        _log = log;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // School Settings — loaded for every report (logo, colour, name)
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<SchoolSettings> GetSchoolSettingsAsync(
        CancellationToken ct = default)
    {
        using var conn = Db();
        await conn.OpenAsync(ct);

        // Single query — pull all relevant settings as key-value pairs
        var rows = await conn.QueryAsync<(string Key, string? Value)>(
            """
            SELECT SettingKey AS Key, SettingValue AS Value
            FROM   dbo.Settings
            WHERE  SettingKey IN (
                'School.Name',   'School.Address', 'School.Phone',
                'School.Email',  'School.Website', 'School.LogoUrl',
                'Theme.PrimaryColor', 'System.Currency', 'System.DateFormat')
            """);

        var d = rows.ToDictionary(r => r.Key, r => r.Value,
                    StringComparer.OrdinalIgnoreCase);

        string Get(string key, string fallback = "") =>
            d.TryGetValue(key, out var v) && v != null ? v : fallback;

        return new SchoolSettings
        {
            Name = Get("School.Name", "School Management Panel"),
            Address = Get("School.Address"),
            Phone = Get("School.Phone"),
            Email = Get("School.Email"),
            Website = Get("School.Website"),
            LogoUrl = Get("School.LogoUrl"),
            PrimaryColor = Get("Theme.PrimaryColor", "#2563EB"),
            Currency = Get("System.Currency", "Rs."),
            DateFormat = Get("System.DateFormat", "dd MMM yyyy")
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Report Card — sp_GetStudentReportCard (3 result sets)
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<ReportCardData> GetReportCardDataAsync(
        Guid studentId, int academicYearId, CancellationToken ct = default)
    {
        using var conn = Db();
        await conn.OpenAsync(ct);

        // Load school settings in parallel with the SP call
        var settingsTask = GetSchoolSettingsAsync(ct);

        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetStudentReportCard",
            new { StudentId = studentId, AcademicYearId = academicYearId },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 30);

        // ── Result set 1: student + school header ──────────────────────────
        var h = await multi.ReadFirstOrDefaultAsync<dynamic>()
             ?? throw new KeyNotFoundException(
                    $"Student {studentId} not found for year {academicYearId}.");

        var header = new ReportCardHeader
        {
            StudentName = (string)h.StudentName,
            RollNumber = (string)h.RollNumber,
            ClassName = (string)h.ClassName,
            Section = (string)h.Section,
            AcademicYear = (string)h.AcademicYear,
            DateOfBirth = h.DateOfBirth is null ? null
                             : ((DateTime)h.DateOfBirth).ToString("dd MMM yyyy"),
            Gender = (string)h.Gender,
            ParentName = (string?)h.ParentName,
            ParentPhone = (string?)h.ParentPhone,
            ProfilePhotoUrl = (string?)h.ProfilePhotoUrl,
            SchoolName = (string)(h.SchoolName ?? string.Empty),
            SchoolAddress = (string?)h.SchoolAddress,
            SchoolPhone = (string?)h.SchoolPhone
        };

        // ── Result set 2: subject result rows ─────────────────────────────
        var subjectRows = (await multi.ReadAsync<ReportCardSubjectRow>()).ToList();

        // ── Result set 3: attendance summary ──────────────────────────────
        var att = await multi.ReadFirstOrDefaultAsync<ReportCardAttendance>()
               ?? new ReportCardAttendance();

        var school = await settingsTask;

        return new ReportCardData
        {
            Header = header,
            Subjects = subjectRows.AsReadOnly(),
            Attendance = att,
            School = school
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fee Receipt — direct join query (no dedicated SP needed)
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<FeeReceiptData> GetFeeReceiptDataAsync(
        long paymentId, CancellationToken ct = default)
    {
        using var conn = Db();
        await conn.OpenAsync(ct);

        var settingsTask = GetSchoolSettingsAsync(ct);

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
                fp.PaidAt           AS PaymentDate,
                u_s.FullName        AS StudentName,
                s.RollNumber,
                c.ClassName + ' - ' + c.Section AS ClassName,
                ft.FeeTypeName,
                u_c.FullName        AS CollectedBy
            FROM   dbo.FeePayments  fp
            JOIN   dbo.Students     s    ON fp.StudentId    = s.StudentId
            JOIN   dbo.Users        u_s  ON s.UserId        = u_s.UserId
            JOIN   dbo.Classes      c    ON s.ClassId       = c.ClassId
            JOIN   dbo.FeeTypes     ft   ON fp.FeeTypeId    = ft.FeeTypeId
            JOIN   dbo.Users        u_c  ON fp.CollectedById = u_c.UserId
            WHERE  fp.PaymentId = @PaymentId AND fp.IsVoided = 0
            """,
            new { PaymentId = paymentId });

        if (row == null)
            throw new KeyNotFoundException($"Payment {paymentId} not found.");

        var school = await settingsTask;

        return new FeeReceiptData
        {
            PaymentId = (long)row.PaymentId,
            ReceiptNumber = (string)row.ReceiptNumber,
            StudentName = (string)row.StudentName,
            RollNumber = (string)row.RollNumber,
            ClassName = (string)row.ClassName,
            FeeTypeName = (string)row.FeeTypeName,
            AmountDue = (decimal)row.AmountDue,
            AmountPaid = (decimal)row.AmountPaid,
            Discount = (decimal)row.Discount,
            Fine = (decimal)row.Fine,
            BalanceDue = (decimal)row.BalanceDue,
            PaymentMethod = (string)row.PaymentMethod,
            ReferenceNumber = (string?)row.ReferenceNumber,
            PaymentDate = (DateTime)row.PaymentDate,
            CollectedBy = (string)row.CollectedBy,
            School = school
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Attendance Sheet — sp_GetAttendanceByClass (2 result sets)
    // Pivots row-per-record into column-per-day for the Excel sheet
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<AttendanceSheetData> GetAttendanceSheetDataAsync(
        int classId, int month, int year, CancellationToken ct = default)
    {
        using var conn = Db();
        await conn.OpenAsync(ct);

        var settingsTask = GetSchoolSettingsAsync(ct);

        var fromDate = new DateTime(year, month, 1);
        var toDate = fromDate.AddMonths(1).AddDays(-1);

        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetAttendanceByClass",
            new
            {
                ClassId = classId,
                FromDate = fromDate,
                ToDate = toDate,
                AcademicYearId = (int?)null
            },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 30);

        // Result set 1: flat attendance rows (one per student per day)
        var raw = (await multi.ReadAsync<dynamic>()).ToList();

        // Result set 2: summary (TotalStudents, TotalPresent, etc.)
        var summary = await multi.ReadFirstOrDefaultAsync<dynamic>();

        // ── Header: class name from first row ─────────────────────────────
        string className = raw.Count > 0 ? (string)(raw[0].ClassName ?? "") : string.Empty;
        string section = raw.Count > 0 ? (string)(raw[0].Section ?? "") : string.Empty;
        string classTeacher = raw.Count > 0 ? (string)(raw[0].MarkedBy ?? "") : string.Empty;

        int daysInMonth = DateTime.DaysInMonth(year, month);

        var sheetHeader = new AttendanceSheetHeader
        {
            ClassName = className,
            Section = section,
            ClassTeacher = classTeacher,
            Month = month,
            Year = year,
            WorkingDays = (int)(summary?.TotalDays ?? 0)
        };

        // ── Pivot: group by student, create day-map ────────────────────────
        var studentGroups = raw
            .GroupBy(r => new {
                RollNumber = (string)r.RollNumber,
                FullName = (string)r.FullName
            })
            .OrderBy(g => g.Key.RollNumber);

        var studentRows = studentGroups.Select(g =>
        {
            var dayMap = new Dictionary<int, string>();
            foreach (var rec in g)
            {
                if (rec.AttendanceDate is DateTime d)
                    dayMap[d.Day] = (string?)rec.Status ?? "";
            }

            int present = dayMap.Values.Count(v => v == "P");
            int absent = dayMap.Values.Count(v => v == "A");
            int leave = dayMap.Values.Count(v => v == "L");
            int totalDays = (int)(summary?.TotalDays ?? 0);
            decimal pct = totalDays == 0 ? 0m
                : Math.Round(present * 100m / totalDays, 1);

            return new AttendanceStudentRow
            {
                RollNumber = g.Key.RollNumber,
                FullName = g.Key.FullName,
                DailyStatus = dayMap,
                PresentCount = present,
                AbsentCount = absent,
                LeaveCount = leave,
                AttendancePct = pct
            };
        }).ToList();

        var school = await settingsTask;

        return new AttendanceSheetData
        {
            Header = sheetHeader,
            Rows = studentRows.AsReadOnly(),
            School = school
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Exam Result Sheet — sp_GetExamResults (2 result sets)
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<ExamResultSheetData> GetExamResultSheetDataAsync(
        int examId, CancellationToken ct = default)
    {
        using var conn = Db();
        await conn.OpenAsync(ct);

        var settingsTask = GetSchoolSettingsAsync(ct);

        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetExamResults",
            new { ExamId = examId },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 30);

        // Result set 1: per-student result rows
        var resultRows = (await multi.ReadAsync<ExamResultStudentRow>()).ToList();

        // Result set 2: class stats
        var stats = await multi.ReadFirstOrDefaultAsync<ExamResultStats>()
                 ?? new ExamResultStats();

        // Load exam header (name, date, class) from a separate lightweight query
        var examInfo = await conn.QueryFirstOrDefaultAsync<dynamic>(
            """
            SELECT  e.ExamName,  e.ExamType,  e.ExamDate,
                    e.TotalMarks, e.PassMarks,
                    sub.SubjectName,
                    c.ClassName,  c.Section,
                    ay.YearName  AS AcademicYear
            FROM    dbo.Exams          e
            JOIN    dbo.Subjects       sub ON e.SubjectId = sub.SubjectId
            JOIN    dbo.Classes        c   ON e.ClassId   = c.ClassId
            JOIN    dbo.AcademicYears  ay  ON e.AcademicYearId = ay.AcademicYearId
            WHERE   e.ExamId = @ExamId
            """,
            new { ExamId = examId });

        if (examInfo == null)
            throw new KeyNotFoundException($"Exam {examId} not found.");

        var header = new ExamResultHeader
        {
            ExamName = (string)examInfo.ExamName,
            ExamType = (string)examInfo.ExamType,
            SubjectName = (string)examInfo.SubjectName,
            ClassName = (string)examInfo.ClassName,
            Section = (string)examInfo.Section,
            ExamDate = (DateTime)examInfo.ExamDate,
            TotalMarks = (decimal)examInfo.TotalMarks,
            PassMarks = (decimal)examInfo.PassMarks,
            AcademicYear = (string)examInfo.AcademicYear
        };

        var school = await settingsTask;

        return new ExamResultSheetData
        {
            Header = header,
            Rows = resultRows.AsReadOnly(),
            Stats = stats,
            School = school
        };
    }

    private SqlConnection Db()
        => new(_config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing."));
}