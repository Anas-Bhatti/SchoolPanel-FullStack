// ============================================================
// Controllers/AttendanceController.cs
//
// Endpoints:
//   POST /api/attendance/mark     bulk mark for a whole class
//   GET  /api/attendance/monthly  student or class, date range
//   GET  /api/attendance/today    dashboard live snapshot
// ============================================================

using System.Data;
using System.Data.SqlClient;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolPanel.Controllers.DTOs;

namespace SchoolPanel.Controllers.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize]
[EnableRateLimiting("PerUser")]
public sealed class AttendanceController : AppControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AttendanceController> _log;

    // Status whitelist — prevents arbitrary values entering the DB
    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "P", "A", "L", "H" };

    public AttendanceController(
        IConfiguration config,
        ILogger<AttendanceController> log)
    {
        _config = config;
        _log = log;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/attendance/mark
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Bulk-mark attendance for an entire class in a single SP call.
    ///
    /// The SP (sp_MarkAttendance) receives all entries as an XML parameter
    /// and performs a MERGE (upsert) — safe to call multiple times on the
    /// same class+date (teachers can correct mistakes).
    ///
    /// Request body example:
    /// {
    ///   "classId": 3,
    ///   "attendanceDate": "2024-11-15",
    ///   "entries": [
    ///     { "studentId": "...", "status": "P" },
    ///     { "studentId": "...", "status": "A", "remarks": "Sick" }
    ///   ]
    /// }
    /// </summary>
    [HttpPost("mark")]
    [Authorize(Policy = "Students.Edit")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 422)]
    public async Task<IActionResult> MarkAttendance(
        [FromBody] MarkAttendanceRequest request,
        CancellationToken ct)
    {
        // ── Validate entries ──────────────────────────────────────────────────
        if (request.Entries.Count == 0)
            return BadRequestProblem("At least one attendance entry is required.");

        // Reject future dates (attendance can only be marked for today or past)
        if (request.AttendanceDate > DateOnly.FromDateTime(DateTime.UtcNow.Date))
            return BadRequestProblem(
                $"Cannot mark attendance for a future date ({request.AttendanceDate}).");

        // Validate all status codes
        var invalidEntries = request.Entries
            .Where(e => !ValidStatuses.Contains(e.Status))
            .Select(e => e.StudentId)
            .ToList();

        if (invalidEntries.Count > 0)
            return BadRequestProblem(
                $"Invalid status code for {invalidEntries.Count} entries. " +
                "Allowed values: P (Present), A (Absent), L (Leave), H (Holiday).");

        // Detect duplicate StudentId entries in the same request
        var duplicates = request.Entries
            .GroupBy(e => e.StudentId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            return BadRequestProblem(
                $"Duplicate student entries detected for {duplicates.Count} student(s).");

        // ── Build XML for bulk SP call ─────────────────────────────────────────
        // SP expects: <rows><row StudentId="..." Status="P" Remarks="..."/></rows>
        var xml = BuildAttendanceXml(request.Entries);

        var markedById = CurrentUserId!.Value;

        using var conn = Db();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_MarkAttendance",
            new
            {
                ClassId = request.ClassId,
                AttendanceDate = request.AttendanceDate.ToDateTime(TimeOnly.MinValue),
                MarkedById = markedById,
                AttendanceXml = xml
            },
            commandType: CommandType.StoredProcedure,
            commandTimeout: 60);   // Bulk upsert for large classes

        string code = result?.ResultCode ?? "ERROR";
        int records = result?.RecordsProcessed ?? 0;

        if (code != "SUCCESS")
            return ServerErrorProblem("Attendance could not be saved. Please retry.");

        _log.LogInformation(
            "Attendance marked. ClassId={C} Date={D} Records={R} By={U}",
            request.ClassId, request.AttendanceDate, records, markedById);

        return Ok(new
        {
            message = "Attendance saved.",
            recordsProcessed = records,
            classId = request.ClassId,
            date = request.AttendanceDate
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/attendance/monthly
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Monthly attendance report.
    /// Can filter by a specific student OR an entire class.
    /// Returns:
    ///   - Row per student per attendance day
    ///   - Daily aggregates (present/absent/leave counts)
    ///   - Monthly summary (attendance %)
    ///
    /// Query params: studentId OR classId, month, year, academicYearId
    /// </summary>
    [HttpGet("monthly")]
    [Authorize(Policy = "Students.View")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetMonthly(
        [FromQuery] MonthlyAttendanceQuery q,
        CancellationToken ct)
    {
        // Must supply either studentId or classId
        if (q.StudentId == null && q.ClassId == null)
            return BadRequestProblem(
                "Provide either 'studentId' or 'classId' as a query parameter.");

        if (q.Month < 1 || q.Month > 12)
            return BadRequestProblem("Month must be between 1 and 12.");

        if (q.Year < 2000 || q.Year > DateTime.UtcNow.Year + 1)
            return BadRequestProblem("Invalid year value.");

        // First and last day of the requested month
        var fromDate = new DateOnly(q.Year, q.Month, 1);
        var toDate = fromDate.AddMonths(1).AddDays(-1);

        using var conn = Db();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetAttendanceByClass",
            new
            {
                ClassId = q.ClassId,
                StudentId = q.StudentId,
                FromDate = fromDate.ToDateTime(TimeOnly.MinValue),
                ToDate = toDate.ToDateTime(TimeOnly.MinValue),
                AcademicYearId = q.AcademicYearId
            },
            commandType: CommandType.StoredProcedure);

        // SP returns: individual records, then daily aggregates
        var records = (await multi.ReadAsync<AttendanceRow>()).ToList();
        var summary = await multi.ReadFirstOrDefaultAsync<dynamic>();

        // Build day-level aggregates from the records
        var dailyBreakdown = records
            .GroupBy(r => r.AttendanceDate)
            .Select(g => new AttendanceDay(
                Date: g.Key,
                DayName: g.Key.DayOfWeek.ToString(),
                TotalStudents: g.Count(),
                Present: g.Count(r => r.Status == "P"),
                Absent: g.Count(r => r.Status == "A"),
                Leave: g.Count(r => r.Status == "L"),
                Holiday: g.Count(r => r.Status == "H"),
                PresentPct: g.Count() == 0 ? 0m :
                               Math.Round(
                                   g.Count(r => r.Status == "P") * 100m / g.Count(), 1)))
            .OrderBy(d => d.Date)
            .ToList();

        return Ok(new
        {
            period = new
            {
                month = q.Month,
                year = q.Year,
                fromDate,
                toDate,
                label = fromDate.ToString("MMMM yyyy")
            },
            summary = summary == null ? null : new
            {
                totalDays = (int)(summary.TotalDays ?? 0),
                totalStudents = (int)(summary.TotalStudents ?? 0),
                totalPresent = (int)(summary.TotalPresent ?? 0),
                totalAbsent = (int)(summary.TotalAbsent ?? 0),
                totalLeave = (int)(summary.TotalLeave ?? 0)
            },
            daily = dailyBreakdown,
            records
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/attendance/today
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Dashboard live snapshot — attendance for today across all classes.
    ///
    /// Returns:
    ///   - School-wide totals (students + teachers)
    ///   - Per-class breakdown for the ApexCharts bar/pie widgets
    ///
    /// This is called by the dashboard on load and can be cached
    /// for up to 5 minutes (ResponseCache attribute).
    /// </summary>
    [HttpGet("today")]
    [Authorize(Policy = "Dashboard.View")]
    [ResponseCache(Duration = 300, VaryByQueryKeys = ["academicYearId"])]
    [ProducesResponseType(typeof(TodayAttendance), 200)]
    public async Task<IActionResult> GetToday(
        [FromQuery] int? academicYearId,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        using var conn = Db();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetDashboardStats",
            new { AcademicYearId = academicYearId },
            commandType: CommandType.StoredProcedure);

        // SP result set 1: school-wide summary
        var summary = await multi.ReadFirstOrDefaultAsync<dynamic>();

        // SP result set 2: fee chart (skip it)
        await multi.ReadAsync<dynamic>();

        // SP result set 3: today's class attendance breakdown
        var classRows = (await multi.ReadAsync<dynamic>()).ToList();

        var classDtos = classRows.Select(c => new ClassAttendanceSnapshot(
            ClassName: (string)c.ClassName,
            Section: (string)c.Section,
            Total: (int)(c.TotalStudents ?? 0),
            Present: (int)(c.Present ?? 0),
            Absent: (int)(c.Absent ?? 0),
            Leave: (int)(c.Leave ?? 0)
        )).ToList();

        int total = classDtos.Sum(c => c.Total);
        int present = classDtos.Sum(c => c.Present);
        int absent = classDtos.Sum(c => c.Absent);
        int leave = classDtos.Sum(c => c.Leave);
        decimal pct = total == 0 ? 0m : Math.Round(present * 100m / total, 1);

        var dto = new TodayAttendance(
            Date: today,
            TotalStudents: total,
            TotalTeachers: (int)(summary?.TotalTeachers ?? 0),
            StudentsPresent: present,
            StudentsAbsent: absent,
            StudentsLeave: leave,
            StudentPresentPct: pct,
            ClassBreakdown: classDtos.AsReadOnly()
        );

        return Ok(dto);
    }

    // ─── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Build the XML string passed to sp_MarkAttendance.
    /// Uses SecurityElement.Escape for remarks to prevent XML injection.
    /// </summary>
    private static string BuildAttendanceXml(
        IReadOnlyList<AttendanceEntry> entries)
    {
        var sb = new StringBuilder(entries.Count * 80);
        sb.Append("<rows>");

        foreach (var e in entries)
        {
            var remarks = System.Security.SecurityElement
                .Escape(e.Remarks ?? string.Empty);

            sb.Append("<row StudentId=\"")
              .Append(e.StudentId)
              .Append("\" Status=\"")
              .Append(e.Status.ToUpperInvariant())
              .Append("\" Remarks=\"")
              .Append(remarks)
              .Append("\"/>");
        }

        sb.Append("</rows>");
        return sb.ToString();
    }

    private SqlConnection Db()
        => new(_config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing."));
}