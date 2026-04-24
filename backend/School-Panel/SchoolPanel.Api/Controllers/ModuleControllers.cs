using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SchoolPanel.Api.Configuration;
using SchoolPanel.Api.DTOs;
using SchoolPanel.Api.Repositories;
using SchoolPanel.Api.Services;
using SchoolPanel.Controllers.DTOs;
using SchoolPanel.Reports.Models;
using System.Security.Claims;
using CreateStudentRequest = SchoolPanel.Api.DTOs.CreateStudentRequest;
using UpdateStudentRequest = SchoolPanel.Api.DTOs.UpdateStudentRequest;
using MarkAttendanceRequest = SchoolPanel.Api.DTOs.MarkAttendanceRequest;
using StudentListItem = SchoolPanel.Api.DTOs.StudentListItem;
using StudentDetail = SchoolPanel.Api.DTOs.StudentDetail;

namespace SchoolPanel.Api.Controllers;

// ═══════════════════════════════════════════════════════════════
// STUDENTS CONTROLLER — Full Implementation
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/students")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class StudentsControllerImpl : ControllerBase
{
    private readonly IStudentRepository _students;
    private readonly IAuditLogRepository _audit;
    private readonly IFileUploadService _upload;
    private readonly SecurityOptions _security;
    private readonly ILogger<StudentsControllerImpl> _logger;

    public StudentsControllerImpl(
        IStudentRepository students,
        IAuditLogRepository audit,
        IFileUploadService upload,
        IOptions<SecurityOptions> security,
        ILogger<StudentsControllerImpl> logger)
    {
        _students = students;
        _audit = audit;
        _upload = upload;
        _security = security.Value;
        _logger = logger;
    }

    /// <summary>GET /api/students — paginated list with filters</summary>
    [HttpGet]
    [Authorize(Policy = "Students.View")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<StudentListItem>>), 200)]
    public async Task<IActionResult> GetStudents(
        [FromQuery] int? classId = null,
        [FromQuery] int? academicYearId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? gender = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (totalCount, items) = await _students.GetStudentsAsync(
            classId, academicYearId, status, search,
            gender, pageNumber, pageSize, ct);

        var paged = new PagedResponse<StudentListItem>(
            Items: items,
            TotalCount: totalCount,
            PageNumber: pageNumber,
            PageSize: pageSize,
            TotalPages: (int)Math.Ceiling(totalCount / (double)pageSize));

        Response.Headers.Append("X-Pagination",
            $"page={pageNumber},size={pageSize},total={totalCount}");

        return Ok(ApiResponse<PagedResponse<StudentListItem>>.Ok(paged));
    }

    /// <summary>GET /api/students/{id} — full profile</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "Students.View")]
    [ProducesResponseType(typeof(ApiResponse<StudentDetail>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetStudent(Guid id, CancellationToken ct)
    {
        var student = await _students.GetStudentByIdAsync(id, ct);
        if (student == null)
            return NotFound(ApiResponse<object>.Fail("Student not found."));
        return Ok(ApiResponse<StudentDetail>.Ok(student));
    }

    /// <summary>POST /api/students — enroll new student</summary>
    [HttpPost]
    [Authorize(Policy = "Students.Create")]
    [ProducesResponseType(typeof(ApiResponse<object>), 201)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> CreateStudent(
        [FromBody] CreateStudentRequest request,
        CancellationToken ct)
    {
        var createdById = GetCurrentUserId()!.Value;
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(
            request.Password, _security.BcryptWorkFactor);

        var (code, studentId, _) = await _students.CreateStudentAsync(
            request, createdById, passwordHash, ct);

        return code switch
        {
            "SUCCESS" => CreatedAtAction(nameof(GetStudent), new { id = studentId },
                              ApiResponse<object>.Ok(new { studentId }, "Student enrolled.")),
            "ROLL_EXISTS" => BadRequest(ApiResponse<object>.Fail(
                              "Roll number already exists in this class and year.")),
            "EMAIL_EXISTS" => BadRequest(ApiResponse<object>.Fail(
                              "A user with this email already exists.")),
            _ => StatusCode(500, ApiResponse<object>.Fail("Enrollment failed."))
        };
    }

    /// <summary>PUT /api/students/{id} — update student</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Students.Edit")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateStudent(
        Guid id,
        [FromBody] UpdateStudentRequest request,
        CancellationToken ct)
    {
        var updatedById = GetCurrentUserId()!.Value;
        var code = await _students.UpdateStudentAsync(id, request, updatedById, ct);

        return code switch
        {
            "SUCCESS" => Ok(ApiResponse<object>.Ok(null, "Student updated.")),
            "NOT_FOUND" => NotFound(ApiResponse<object>.Fail("Student not found.")),
            _ => StatusCode(500, ApiResponse<object>.Fail("Update failed."))
        };
    }

    /// <summary>DELETE /api/students/{id} — soft delete</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Students.Delete")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteStudent(Guid id, CancellationToken ct)
    {
        var deletedById = GetCurrentUserId()!.Value;
        var code = await _students.DeleteStudentAsync(id, deletedById, ct);

        return code switch
        {
            "SUCCESS" => Ok(ApiResponse<object>.Ok(null, "Student removed.")),
            "NOT_FOUND" => NotFound(ApiResponse<object>.Fail("Student not found.")),
            _ => StatusCode(500, ApiResponse<object>.Fail("Delete failed."))
        };
    }

    /// <summary>POST /api/students/attendance — bulk mark class attendance</summary>
    [HttpPost("attendance")]
    [Authorize(Policy = "Students.Edit")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> MarkAttendance(
        [FromBody] MarkAttendanceRequest request,
        CancellationToken ct)
    {
        if (request.Entries.Count == 0)
            return BadRequest(ApiResponse<object>.Fail("No attendance entries provided."));

        var markedById = GetCurrentUserId()!.Value;
        var code = await _students.MarkAttendanceAsync(request, markedById, ct);

        return code == "SUCCESS"
            ? Ok(ApiResponse<object>.Ok(null, "Attendance marked."))
            : StatusCode(500, ApiResponse<object>.Fail("Attendance marking failed."));
    }

    /// <summary>GET /api/students/{id}/attendance — student attendance summary</summary>
    [HttpGet("{id:guid}/attendance")]
    [Authorize(Policy = "Students.View")]
    [ProducesResponseType(typeof(ApiResponse<SchoolPanel.Api.DTOs.AttendanceSummaryDto>), 200)]
    public async Task<IActionResult> GetAttendance(
        Guid id,
        [FromQuery] int? academicYearId,
        CancellationToken ct)
    {
        var summary = await _students.GetStudentAttendanceSummaryAsync(id, academicYearId, ct);
        return Ok(ApiResponse<SchoolPanel.Api.DTOs.AttendanceSummaryDto>.Ok(summary));
    }

    /// <summary>GET /api/students/attendance/class — class attendance for a date range</summary>
    [HttpGet("attendance/class")]
    [Authorize(Policy = "Students.View")]
    [ProducesResponseType(typeof(ApiResponse<ClassAttendance>), 200)]
    public async Task<IActionResult> GetClassAttendance(
        [FromQuery] int classId,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly? toDate = null,
        [FromQuery] int? academicYearId = null,
        CancellationToken ct = default)
    {
        var result = await _students.GetAttendanceByClassAsync(
            classId, fromDate, toDate, academicYearId, ct);
        return Ok(ApiResponse<ClassAttendance>.Ok(result));
    }

    /// <summary>POST /api/students/{id}/photo — upload profile photo</summary>
    [HttpPost("{id:guid}/photo")]
    [Authorize(Policy = "Students.Edit")]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResult>), 200)]
    public async Task<IActionResult> UploadPhoto(
        Guid id,
        IFormFile file,
        CancellationToken ct)
    {
        var result = await _upload.UploadAsync(
            file, UploadFolder.StudentPhotos, $"student_{id}", ct);

        if (!result.Success)
            return BadRequest(ApiResponse<object>.Fail(result.Error ?? "Upload failed."));

        var updatedById = GetCurrentUserId()!.Value;
        await _students.UpdateStudentAsync(id,
            new UpdateStudentRequest(ProfilePhotoUrl: result.Url),
            updatedById, ct);

        return Ok(ApiResponse<FileUploadResult>.Ok(
            new FileUploadResult(result.Success, result.Url, result.BlobName, result.Error),
            "Photo uploaded."));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

// ═══════════════════════════════════════════════════════════════
// TEACHERS CONTROLLER — Full Implementation
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/teachers")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class TeachersControllerImpl : ControllerBase
{
    private readonly ITeacherRepository _teachers;
    private readonly IAuditLogRepository _audit;
    private readonly IFileUploadService _upload;
    private readonly SecurityOptions _security;

    public TeachersControllerImpl(
        ITeacherRepository teachers,
        IAuditLogRepository audit,
        IFileUploadService upload,
        IOptions<SecurityOptions> security)
    {
        _teachers = teachers;
        _audit = audit;
        _upload = upload;
        _security = security.Value;
    }

    [HttpGet]
    [Authorize(Policy = "Teachers.View")]
    public async Task<IActionResult> GetTeachers(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (totalCount, items) = await _teachers.GetTeachersAsync(
            search, status, pageNumber, pageSize, ct);

        var paged = new PagedResponse<TeacherListItem>(
            items, totalCount, pageNumber, pageSize,
            (int)Math.Ceiling(totalCount / (double)pageSize));

        Response.Headers.Append("X-Pagination",
            $"page={pageNumber},size={pageSize},total={totalCount}");

        return Ok(ApiResponse<PagedResponse<TeacherListItem>>.Ok(paged));
    }

    [HttpPost]
    [Authorize(Policy = "Teachers.Create")]
    public async Task<IActionResult> CreateTeacher(
        [FromBody] CreateTeacherRequest request,
        CancellationToken ct)
    {
        var createdById = GetCurrentUserId()!.Value;
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(
            request.Password, _security.BcryptWorkFactor);

        var (code, teacherId, _) = await _teachers.CreateTeacherAsync(
            request, passwordHash, createdById, ct);

        return code switch
        {
            "SUCCESS" => StatusCode(201, ApiResponse<object>.Ok(
                                     new { teacherId }, "Teacher created.")),
            "EMAIL_EXISTS" => BadRequest(ApiResponse<object>.Fail(
                                     "Email already registered.")),
            "EMPLOYEE_CODE_EXISTS" => BadRequest(ApiResponse<object>.Fail(
                                     "Employee code already exists.")),
            _ => StatusCode(500, ApiResponse<object>.Fail("Creation failed."))
        };
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Teachers.Edit")]
    public async Task<IActionResult> UpdateTeacher(
        Guid id,
        [FromBody] UpdateTeacherRequest request,
        CancellationToken ct)
    {
        var updatedById = GetCurrentUserId()!.Value;
        var code = await _teachers.UpdateTeacherAsync(id, request, updatedById, ct);
        return code == "SUCCESS"
            ? Ok(ApiResponse<object>.Ok(null, "Teacher updated."))
            : StatusCode(500, ApiResponse<object>.Fail("Update failed."));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Teachers.Delete")]
    public async Task<IActionResult> DeleteTeacher(Guid id, CancellationToken ct)
    {
        var deletedById = GetCurrentUserId()!.Value;
        var code = await _teachers.DeleteTeacherAsync(id, deletedById, ct);
        return code == "SUCCESS"
            ? Ok(ApiResponse<object>.Ok(null, "Teacher removed."))
            : StatusCode(500, ApiResponse<object>.Fail("Delete failed."));
    }

    [HttpPost("{id:guid}/cv")]
    [Authorize(Policy = "Teachers.Edit")]
    public async Task<IActionResult> UploadCV(Guid id, IFormFile file, CancellationToken ct)
    {
        var result = await _upload.UploadAsync(
            file, UploadFolder.TeacherDocuments, $"cv_{id}", ct);

        if (!result.Success)
            return BadRequest(ApiResponse<object>.Fail(result.Error ?? "Upload failed."));

        var updatedById = GetCurrentUserId()!.Value;
        await _teachers.UpdateTeacherAsync(id,
            new UpdateTeacherRequest(CVDocumentUrl: result.Url),
            updatedById, ct);

        return Ok(ApiResponse<FileUploadResult>.Ok(
            new FileUploadResult(result.Success, result.Url, result.BlobName, result.Error),
            "CV uploaded."));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

// ═══════════════════════════════════════════════════════════════
// FEES CONTROLLER — Full Implementation
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/fees")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class FeesControllerImpl : ControllerBase
{
    private readonly IFeeRepository _fees;
    private readonly IAuditLogRepository _audit;

    public FeesControllerImpl(IFeeRepository fees, IAuditLogRepository audit)
    {
        _fees = fees;
        _audit = audit;
    }

    [HttpGet("student/{studentId:guid}")]
    [Authorize(Policy = "Fees.View")]
    [ProducesResponseType(typeof(ApiResponse<StudentFeeStatus>), 200)]
    public async Task<IActionResult> GetStudentFeeStatus(
        Guid studentId,
        [FromQuery] int? academicYearId = null,
        CancellationToken ct = default)
    {
        var result = await _fees.GetStudentFeeStatusAsync(studentId, academicYearId, ct);
        return result == null
            ? NotFound(ApiResponse<object>.Fail("Student not found."))
            : Ok(ApiResponse<StudentFeeStatus>.Ok(result));
    }

    [HttpPost("payment")]
    [Authorize(Policy = "Fees.Create")]
    [ProducesResponseType(typeof(ApiResponse<object>), 201)]
    public async Task<IActionResult> RecordPayment(
        [FromBody] DTOs.RecordPaymentRequest request,
        CancellationToken ct)
    {
        var collectedById = GetCurrentUserId()!.Value;
        var (code, paymentId, receiptNumber) = await _fees.RecordPaymentAsync(
            request, collectedById, ct);

        if (code != "SUCCESS")
            return StatusCode(500, ApiResponse<object>.Fail("Payment recording failed."));

        await _audit.InsertAsync(
            userId: collectedById,
            userEmail: User.FindFirstValue(ClaimTypes.Email),
            action: "Create",
            module: "Fees",
            recordId: paymentId?.ToString(),
            oldValue: null,
            newValue: $"Receipt:{receiptNumber},Amount:{request.AmountPaid}",
            description: $"Fee payment recorded. Receipt: {receiptNumber}",
            ipAddress: GetClientIp(),
            userAgent: Request.Headers["User-Agent"]);

        return StatusCode(201, ApiResponse<object>.Ok(
            new { paymentId, receiptNumber },
            "Payment recorded successfully."));
    }

    [HttpGet("summary")]
    [Authorize(Policy = "Fees.View")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> GetFeeSummary(
        [FromQuery] int academicYearId,
        [FromQuery] int? classId = null,
        [FromQuery] int? month = null,
        [FromQuery] int? year = null,
        CancellationToken ct = default)
    {
        var (monthly, classWise) = await _fees.GetFeeCollectionSummaryAsync(
            academicYearId, classId, month, year, ct);

        return Ok(ApiResponse<object>.Ok(new { monthly, classWise }));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private string GetClientIp()
    {
        var fwd = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        return !string.IsNullOrEmpty(fwd)
            ? fwd.Split(',')[0].Trim()
            : HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

// ═══════════════════════════════════════════════════════════════
// EXAMS CONTROLLER — Full Implementation
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/exams")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class ExamsControllerImpl : ControllerBase
{
    private readonly IExamRepository _exams;
    private readonly IAuditLogRepository _audit;
    private readonly IFileUploadService _upload;

    public ExamsControllerImpl(
        IExamRepository exams,
        IAuditLogRepository audit,
        IFileUploadService upload)
    {
        _exams = exams;
        _audit = audit;
        _upload = upload;
    }

    [HttpPost("{examId:int}/results")]
    [Authorize(Policy = "Exams.Edit")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> SaveResults(
        int examId,
        [FromBody] SaveExamResultsRequest request,
        CancellationToken ct)
    {
        if (request.ExamId != examId)
            return BadRequest(ApiResponse<object>.Fail("ExamId mismatch."));

        var enteredById = GetCurrentUserId()!.Value;
        var code = await _exams.SaveExamResultsAsync(request, enteredById, ct);

        return code == "SUCCESS"
            ? Ok(ApiResponse<object>.Ok(null, $"Results saved for {request.Results.Count} students."))
            : StatusCode(500, ApiResponse<object>.Fail("Saving results failed."));
    }

    [HttpGet("{examId:int}/results")]
    [Authorize(Policy = "Exams.View")]
    [ProducesResponseType(typeof(ApiResponse<ExamResultsResponse>), 200)]
    public async Task<IActionResult> GetResults(
        int examId,
        [FromQuery] int? classId = null,
        CancellationToken ct = default)
    {
        var results = await _exams.GetExamResultsAsync(examId, classId, ct);
        return results == null
            ? NotFound(ApiResponse<object>.Fail("No results found for this exam."))
            : Ok(ApiResponse<ExamResultsResponse>.Ok(results));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

// ═══════════════════════════════════════════════════════════════
// REPORTS CONTROLLER — Full Implementation
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/reports")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class ReportsControllerImpl : ControllerBase
{
    private readonly IExamRepository _exams;
    private readonly IDashboardRepository _dashboard;

    public ReportsControllerImpl(
        IExamRepository exams,
        IDashboardRepository dashboard)
    {
        _exams = exams;
        _dashboard = dashboard;
    }

    /// <summary>GET /api/reports/report-card/{studentId} — JSON data for PDF generation</summary>
    [HttpGet("report-card/{studentId:guid}")]
    [Authorize(Policy = "Reports.View")]
    [ProducesResponseType(typeof(ApiResponse<ReportCard>), 200)]
    public async Task<IActionResult> GetReportCard(
        Guid studentId,
        [FromQuery] int academicYearId,
        [FromQuery] string? examType = null,
        CancellationToken ct = default)
    {
        var card = await _exams.GetStudentReportCardAsync(studentId, academicYearId, examType, ct);
        return card == null
            ? NotFound(ApiResponse<object>.Fail("Report card data not found."))
            : Ok(ApiResponse<ReportCard>.Ok(card));
    }

    /// <summary>GET /api/reports/attendance — attendance report data</summary>
    [HttpGet("attendance")]
    [Authorize(Policy = "Reports.View")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<AttendanceReportRowDto>>), 200)]
    public async Task<IActionResult> GetAttendanceReport(
        [FromQuery] int academicYearId,
        [FromQuery] int? classId = null,
        [FromQuery] Guid? studentId = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        var rows = await _dashboard.GetAttendanceReportAsync(
            academicYearId, classId, studentId, fromDate, toDate, ct);
        return Ok(ApiResponse<IEnumerable<AttendanceReportRowDto>>.Ok(rows));
    }

    /// <summary>GET /api/reports/strength — student strength by class</summary>
    [HttpGet("strength")]
    [Authorize(Policy = "Reports.View")]
    [ProducesResponseType(typeof(ApiResponse<StudentStrengthReport>), 200)]
    public async Task<IActionResult> GetStrengthReport(
        [FromQuery] int? academicYearId = null,
        CancellationToken ct = default)
    {
        var report = await _dashboard.GetStrengthReportAsync(academicYearId, ct);
        return report == null
            ? NotFound(ApiResponse<object>.Fail("No data found."))
            : Ok(ApiResponse<StudentStrengthReport>.Ok(report));
    }
}

// ═══════════════════════════════════════════════════════════════
// DASHBOARD CONTROLLER — Full Implementation
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/dashboard")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class DashboardControllerImpl : ControllerBase
{
    private readonly IDashboardRepository _dashboard;

    public DashboardControllerImpl(IDashboardRepository dashboard)
        => _dashboard = dashboard;

    [HttpGet("stats")]
    [Authorize(Policy = "Dashboard.View")]
    [ResponseCache(Duration = 60, VaryByHeader = "Authorization")]
    [ProducesResponseType(typeof(ApiResponse<DashboardStats>), 200)]
    public async Task<IActionResult> GetStats(
        [FromQuery] int? academicYearId = null,
        CancellationToken ct = default)
    {
        var stats = await _dashboard.GetStatsAsync(academicYearId, ct);
        return stats == null
            ? StatusCode(500, ApiResponse<object>.Fail("Failed to load dashboard stats."))
            : Ok(ApiResponse<DashboardStats>.Ok(stats));
    }
}

// ═══════════════════════════════════════════════════════════════
// ROLES CONTROLLER — Full Implementation
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/roles")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class RolesControllerImpl : ControllerBase
{
    private readonly IRolesRepository _roles;
    private readonly IAuditLogRepository _audit;

    public RolesControllerImpl(IRolesRepository roles, IAuditLogRepository audit)
    {
        _roles = roles;
        _audit = audit;
    }

    [HttpGet]
    [Authorize(Policy = "Roles.View")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<RoleDto>>), 200)]
    public async Task<IActionResult> GetRoles(CancellationToken ct)
    {
        var roles = await _roles.GetAllRolesAsync(ct);
        return Ok(ApiResponse<IEnumerable<RoleDto>>.Ok(roles));
    }

    [HttpPost]
    [Authorize(Policy = "Roles.Create")]
    [ProducesResponseType(typeof(ApiResponse<object>), 201)]
    public async Task<IActionResult> CreateRole(
        [FromBody] CreateRoleRequest request,
        CancellationToken ct)
    {
        var createdById = GetCurrentUserId()!.Value;
        var (code, roleId) = await _roles.CreateRoleAsync(request, createdById, ct);

        return code switch
        {
            "SUCCESS" => StatusCode(201, ApiResponse<object>.Ok(
                            new { roleId }, "Role created.")),
            "ROLE_EXISTS" => Conflict(ApiResponse<object>.Fail(
                            "A role with this name already exists.")),
            _ => StatusCode(500, ApiResponse<object>.Fail("Creation failed."))
        };
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "Roles.Edit")]
    public async Task<IActionResult> UpdateRole(
        int id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken ct)
    {
        var code = await _roles.UpdateRoleAsync(id, request, ct);
        return code == "SUCCESS"
            ? Ok(ApiResponse<object>.Ok(null, "Role updated."))
            : StatusCode(500, ApiResponse<object>.Fail("Update failed."));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Roles.Delete")]
    public async Task<IActionResult> DeleteRole(int id, CancellationToken ct)
    {
        var code = await _roles.DeleteRoleAsync(id, ct);
        return code switch
        {
            "SUCCESS" => Ok(ApiResponse<object>.Ok(null, "Role deleted.")),
            "CANNOT_DELETE_SYSTEM_ROLE" => Forbid(),
            "ROLE_HAS_USERS" => Conflict(ApiResponse<object>.Fail(
                                          "Cannot delete role — users are assigned to it.")),
            _ => StatusCode(500, ApiResponse<object>.Fail("Delete failed."))
        };
    }

    [HttpPut("{id:int}/permissions")]
    [Authorize(Policy = "Roles.Edit")]
    public async Task<IActionResult> SetPermissions(
        int id,
        [FromBody] SetPermissionsRequest request,
        CancellationToken ct)
    {
        if (request.RoleId != id)
            return BadRequest(ApiResponse<object>.Fail("RoleId mismatch."));

        await _roles.SetPermissionsAsync(request, ct);
        return Ok(ApiResponse<object>.Ok(null, "Permissions updated."));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

// ═══════════════════════════════════════════════════════════════
// SETTINGS CONTROLLER — Full Implementation
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/settings")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class SettingsControllerImpl : ControllerBase
{
    private readonly ISettingsRepository _settings;
    private readonly IAuditLogRepository _audit;

    public SettingsControllerImpl(ISettingsRepository settings, IAuditLogRepository audit)
    {
        _settings = settings;
        _audit = audit;
    }

    [HttpGet]
    [Authorize(Policy = "Settings.View")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<SettingDto>>), 200)]
    public async Task<IActionResult> GetSettings(
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        var settings = await _settings.GetByCategory(category, ct);
        return Ok(ApiResponse<IEnumerable<SettingDto>>.Ok(settings));
    }

    [HttpPut]
    [Authorize(Policy = "Settings.Edit")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> UpdateSetting(
        [FromBody] UpdateSettingRequest request,
        CancellationToken ct)
    {
        var updatedById = GetCurrentUserId()!.Value;

        // Get old value for audit trail
        var old = await _settings.GetByKey(request.Key, ct);
        await _settings.UpsertAsync(request, updatedById, ct);

        await _audit.InsertAsync(
            userId: updatedById,
            userEmail: User.FindFirstValue(ClaimTypes.Email),
            action: "SettingsChanged",
            module: "Settings",
            recordId: request.Key,
            oldValue: old?.SettingValue,
            newValue: request.Value,
            description: $"Setting '{request.Key}' updated",
            ipAddress: Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            userAgent: Request.Headers["User-Agent"]);

        return Ok(ApiResponse<object>.Ok(null, "Setting updated."));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

// ═══════════════════════════════════════════════════════════════
// NOTIFICATIONS CONTROLLER — Full Implementation
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/notifications")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class NotificationsControllerImpl : ControllerBase
{
    private readonly INotificationRepository _notifications;

    public NotificationsControllerImpl(INotificationRepository notifications)
        => _notifications = notifications;

    [HttpGet]
    [Authorize(Policy = "Notifications.View")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] bool? isRead = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId == null) return Unauthorized();

        var (unread, notifications) = await _notifications.GetNotificationsAsync(
            userId.Value, isRead, pageNumber, pageSize, ct);

        return Ok(ApiResponse<object>.Ok(new { unreadCount = unread, notifications }));
    }

    [HttpPost]
    [Authorize(Policy = "Notifications.Create")]
    [ProducesResponseType(typeof(ApiResponse<object>), 201)]
    public async Task<IActionResult> CreateNotification(
        [FromBody] CreateNotificationRequest request,
        CancellationToken ct)
    {
        if (request.TargetUserId == null && request.TargetRoleId == null)
            return BadRequest(ApiResponse<object>.Fail(
                "Either TargetUserId or TargetRoleId must be specified."));

        var createdById = GetCurrentUserId()!.Value;
        await _notifications.CreateAsync(request, createdById, ct);
        return StatusCode(201, ApiResponse<object>.Ok(null, "Notification sent."));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

// ═══════════════════════════════════════════════════════════════
// UPLOAD CONTROLLER — General file upload endpoint
// ═══════════════════════════════════════════════════════════════

[ApiController]
[Route("api/upload")]
[Authorize]
[EnableRateLimiting("PerUser")]
[Produces("application/json")]
public sealed class UploadController : ControllerBase
{
    private readonly IFileUploadService _upload;

    public UploadController(IFileUploadService upload)
        => _upload = upload;

    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]   // 20 MB hard cap
    [ProducesResponseType(typeof(ApiResponse<FileUploadResult>), 200)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromQuery] string folder = "StudentPhotos",
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("No file provided."));

        if (!Enum.TryParse<UploadFolder>(folder, true, out var uploadFolder))
            return BadRequest(ApiResponse<object>.Fail(
                $"Invalid folder. Valid values: {string.Join(", ", Enum.GetNames<UploadFolder>())}"));

        var result = await _upload.UploadAsync(file, uploadFolder, ct: ct);

        return result.Success
            ? Ok(ApiResponse<FileUploadResult>.Ok(
                new FileUploadResult(result.Success, result.Url, result.BlobName, result.Error)))
            : BadRequest(ApiResponse<FileUploadResult>.Fail(
                result.Error ?? "Upload failed."));
    }
}