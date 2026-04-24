// ============================================================
// Controllers/StudentsController.cs
//
// Attribute routing chosen over Minimal APIs:
// - 6 endpoints with complex auth, file uploads, model binding,
//   filters, and DI — controllers keep this clean at scale.
// - [ServiceFilter(typeof(LoginLockoutFilter))] and similar
//   filters attach cleanly to specific actions.
// - Swagger ProducesResponseType attributes work naturally.
//
// Endpoints:
//   GET    /api/students            paginated + filtered list
//   GET    /api/students/{id}       full profile
//   POST   /api/students            create student
//   PUT    /api/students/{id}       update + optional photo upload
//   DELETE /api/students/{id}       soft delete
//   POST   /api/students/bulk       Excel import
//   GET    /api/students/template   download blank import template
// ============================================================

using System.Data;
using System.Data.SqlClient;
using System.Text;
using BCrypt.Net;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolPanel.Controllers.DTOs;
using SchoolPanel.Controllers.Services;

namespace SchoolPanel.Controllers.Controllers;

[ApiController]
[Route("api/legacy/students")]
[Authorize]
[EnableRateLimiting("PerUser")]
public sealed class StudentsController : AppControllerBase
{
    private readonly IConfiguration _config;
    private readonly IBlobStorageService _blob;
    private readonly IExcelImportService _excel;
    private readonly SecuritySettings _security;
    private readonly ILogger<StudentsController> _log;

    public StudentsController(
        IConfiguration config,
        IBlobStorageService blob,
        IExcelImportService excel,
        IOptions<SecuritySettings> security,
        ILogger<StudentsController> log)
    {
        _config = config;
        _blob = blob;
        _excel = excel;
        _security = security.Value;
        _log = log;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/students
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Paginated, searchable, filterable student list.
    /// Server-side via sp_GetStudents with OFFSET/FETCH.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "Students.View")]
    [ProducesResponseType(typeof(PagedResult<StudentListItem>), 200)]
    public async Task<IActionResult> GetStudents(
        [FromQuery] StudentFilters q,
        CancellationToken ct)
    {
        q = q with
        {
            PageSize = Math.Clamp(q.PageSize, 1, 200),
            Page = Math.Max(1, q.Page)
        };

        using var conn = Db();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetStudents",
            new
            {
                SearchTerm = q.Search,
                ClassId = q.ClassId,
                Section = q.Section,
                Status = q.Status,
                Gender = q.Gender,
                AcademicYearId = q.AcademicYearId,
                PageNumber = q.Page,
                PageSize = q.PageSize,
                SortColumn = SanitiseSort(q.Sort, "RollNumber"),
                SortDirection = q.IsDescending ? "DESC" : "ASC"
            },
            commandType: CommandType.StoredProcedure);

        var totalCount = await multi.ReadFirstAsync<int>();
        var items = (await multi.ReadAsync<StudentListItem>()).ToList();

        SetPaginationHeader(totalCount, q.Page, q.PageSize);

        return Ok(PagedResult<StudentListItem>.From(items, totalCount, q.Page, q.PageSize));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/students/{id}
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Full student profile with:
    /// - Personal details
    /// - Attendance summary (% present, absent, leave)
    /// - Fee dues summary (total due / paid / balance)
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "Students.View")]
    [ProducesResponseType(typeof(StudentDetail), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetStudent(Guid id, CancellationToken ct)
    {
        using var conn = Db();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetStudentById",
            new { StudentId = id },
            commandType: CommandType.StoredProcedure);

        // Result set 1: profile
        var profile = await multi.ReadFirstOrDefaultAsync<dynamic>();
        if (profile == null)
            return NotFoundProblem($"Student '{id}' not found.");

        // Result set 2: attendance summary
        var att = await multi.ReadFirstOrDefaultAsync<dynamic>();

        // Result set 3: fee summary
        var fee = await multi.ReadFirstOrDefaultAsync<dynamic>();

        var dto = new StudentDetail(
            StudentId: (Guid)profile.StudentId,
            RollNumber: (string)profile.RollNumber,
            FullName: (string)profile.FullName,
            Email: (string)profile.Email,
            PhoneNumber: (string?)profile.PhoneNumber,
            ClassName: (string)profile.ClassName,
            Section: (string)profile.Section,
            ClassId: (int)profile.ClassId,
            Gender: (string)profile.Gender,
            BloodGroup: (string?)profile.BloodGroup,
            DateOfBirth: profile.DateOfBirth is null ? null
                              : DateOnly.FromDateTime((DateTime)profile.DateOfBirth),
            Address: (string?)profile.Address,
            EmergencyContact: (string?)profile.EmergencyContact,
            Status: (string)profile.Status,
            EnrollmentDate: DateOnly.FromDateTime((DateTime)profile.EnrollmentDate),
            ProfilePhotoUrl: (string?)profile.ProfilePhotoUrl,
            AcademicYear: (string)profile.AcademicYear,
            AcademicYearId: (int)profile.AcademicYearId,
            ParentName: (string?)profile.ParentName,
            ParentPhone: (string?)profile.ParentPhone,

            Attendance: att == null
                ? new AttendanceSummary(0, 0, 0, 0, 0m)
                : new AttendanceSummary(
                    (int)att.TotalDays,
                    (int)att.PresentDays,
                    (int)att.AbsentDays,
                    (int)att.LeaveDays,
                    (decimal)att.AttendancePct),

            Fees: fee == null
                ? new FeesSummary(0m, 0m, 0m)
                : new FeesSummary(
                    (decimal)fee.TotalDue,
                    (decimal)fee.TotalPaid,
                    (decimal)fee.BalanceDue)
        );

        return Ok(dto);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/students
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Enroll a new student.
    /// Creates: Users row + UserRoles (Student) + Students row — all in one
    /// SP transaction so either everything succeeds or nothing does.
    /// Password is BCrypt-hashed in C# (work factor from config).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "Students.Create")]
    [ProducesResponseType(typeof(object), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> CreateStudent(
        [FromBody] CreateStudentRequest request,
        CancellationToken ct)
    {
        var createdById = CurrentUserId!.Value;
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(
            request.Password, _security.BcryptWorkFactor);

        using var conn = Db();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_CreateStudent",
            new
            {
                Email = request.Email,
                PasswordHash = passwordHash,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                RollNumber = request.RollNumber,
                ClassId = request.ClassId,
                AcademicYearId = request.AcademicYearId,
                ParentId = request.ParentId,
                DateOfBirth = request.DateOfBirth.HasValue
                                   ? request.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue)
                                   : (DateTime?)null,
                Gender = request.Gender,
                BloodGroup = request.BloodGroup,
                Address = request.Address,
                EmergencyContact = request.EmergencyContact,
                CreatedById = createdById
            },
            commandType: CommandType.StoredProcedure);

        string code = result?.ResultCode ?? "ERROR";
        Guid? studentId = result?.StudentId;

        return code switch
        {
            "SUCCESS" => CreatedAtAction(
                nameof(GetStudent),
                new { id = studentId },
                new { studentId, message = "Student enrolled successfully." }),

            "ROLL_EXISTS" => ConflictProblem(
                $"Roll number '{request.RollNumber}' already exists in this class and year."),

            "EMAIL_EXISTS" => ConflictProblem(
                $"Email '{request.Email}' is already registered."),

            _ => ServerErrorProblem("Enrollment failed. Please try again.")
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PUT /api/students/{id}
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Update student profile fields.
    /// Optionally accepts a multipart photo upload in the same request.
    /// Photo is validated, uploaded to Azure Blob, and the URL saved to DB.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Students.Edit")]
    [Consumes("application/json", "multipart/form-data")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> UpdateStudent(
        Guid id,
        [FromForm] UpdateStudentRequest request,
        IFormFile? photo,
        CancellationToken ct)
    {
        var updatedById = CurrentUserId!.Value;
        string? photoUrl = null;

        // ── Handle optional photo upload ──────────────────────────────────────
        if (photo != null && photo.Length > 0)
        {
            var uploadResult = await _blob.UploadPhotoAsync(
                photo, BlobFolder.StudentPhotos,
                $"student_{id}", ct);

            if (!uploadResult.Success)
                return BadRequestProblem(uploadResult.Error ?? "Photo upload failed.");

            photoUrl = uploadResult.Url;
            _log.LogInformation(
                "Photo uploaded for student {Id}. BlobName={N}", id, uploadResult.BlobName);
        }

        using var conn = Db();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_UpdateStudent",
            new
            {
                StudentId = id,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                ClassId = request.ClassId,
                DateOfBirth = request.DateOfBirth.HasValue
                                   ? request.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue)
                                   : (DateTime?)null,
                Gender = request.Gender,
                BloodGroup = request.BloodGroup,
                Address = request.Address,
                EmergencyContact = request.EmergencyContact,
                ProfilePhotoUrl = photoUrl,
                Status = request.Status,
                UpdatedById = updatedById
            },
            commandType: CommandType.StoredProcedure);

        string code = result?.ResultCode ?? "ERROR";

        return code switch
        {
            "SUCCESS" => Ok(new { message = "Student updated.", photoUrl }),
            "NOT_FOUND" => NotFoundProblem($"Student '{id}' not found."),
            _ => ServerErrorProblem("Update failed.")
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DELETE /api/students/{id}
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Soft-delete student.
    /// Sets IsDeleted=1 on Users + Students, IsActive=0 on Users.
    /// Historical records (attendance, fees, results) are preserved.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Students.Delete")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> DeleteStudent(Guid id, CancellationToken ct)
    {
        var deletedById = CurrentUserId!.Value;

        using var conn = Db();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_SoftDeleteStudent",
            new { StudentId = id, DeletedById = deletedById },
            commandType: CommandType.StoredProcedure);

        string code = result?.ResultCode ?? "ERROR";

        return code switch
        {
            "SUCCESS" => Ok(new { message = "Student removed." }),
            "NOT_FOUND" => NotFoundProblem($"Student '{id}' not found."),
            _ => ServerErrorProblem("Delete failed.")
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/students/bulk
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Bulk enroll students from an Excel (.xlsx) file.
    ///
    /// Process:
    ///   1. Parse and validate every row in the workbook.
    ///   2. For valid rows: BCrypt hash password + call sp_CreateStudent.
    ///   3. Collect per-row errors without aborting the whole batch.
    ///   4. Return { totalRows, succeeded, failed, errors[] }.
    ///
    /// Download the template first via GET /api/students/template.
    /// </summary>
    [HttpPost("bulk")]
    [Authorize(Policy = "Students.Create")]
    [RequestSizeLimit(20 * 1024 * 1024)]  // 20 MB
    [ProducesResponseType(typeof(BulkImportResult), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> BulkImport(
        IFormFile file,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequestProblem("No file provided.");

        // ── Parse & validate Excel ─────────────────────────────────────────────
        var parsed = await _excel.ParseStudentFileAsync(file, ct);

        if (parsed.ValidRows.Count == 0 && parsed.Errors.Count > 0)
            return UnprocessableProblem(
                $"File could not be parsed: {parsed.Errors[0].Reason}");

        var createdById = CurrentUserId!.Value;
        var succeeded = 0;
        var rowErrors = parsed.Errors.ToList();

        // ── Process each valid row ─────────────────────────────────────────────
        foreach (var row in parsed.ValidRows)
        {
            // BCrypt is expensive — run in parallel but cap concurrency
            // to avoid overwhelming the DB connection pool
            try
            {
                var hash = BCrypt.Net.BCrypt.HashPassword(
                    row.Password, _security.BcryptWorkFactor);

                using var conn = Db();
                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "dbo.sp_CreateStudent",
                    new
                    {
                        Email = row.Email,
                        PasswordHash = hash,
                        FullName = row.FullName,
                        PhoneNumber = row.PhoneNumber,
                        RollNumber = row.RollNumber,
                        ClassId = row.ClassId,
                        AcademicYearId = row.AcademicYearId,
                        ParentId = (Guid?)null,
                        DateOfBirth = row.DateOfBirth.HasValue
                                           ? row.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue)
                                           : (DateTime?)null,
                        Gender = row.Gender,
                        BloodGroup = row.BloodGroup,
                        Address = row.Address,
                        EmergencyContact = row.EmergencyContact,
                        CreatedById = createdById
                    },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 30);

                string code = result?.ResultCode ?? "ERROR";

                if (code == "SUCCESS")
                {
                    succeeded++;
                }
                else
                {
                    var reason = code switch
                    {
                        "ROLL_EXISTS" => $"Roll number '{row.RollNumber}' already exists.",
                        "EMAIL_EXISTS" => $"Email '{row.Email}' already registered.",
                        _ => "Database error."
                    };
                    rowErrors.Add(new BulkRowError(row.RowNumber, row.RollNumber, reason));
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Bulk import: SP call failed for row {Row} Roll={Roll}",
                    row.RowNumber, row.RollNumber);

                rowErrors.Add(new BulkRowError(
                    row.RowNumber, row.RollNumber, "Unexpected error."));
            }
        }

        var totalRows = parsed.ValidRows.Count + parsed.Errors.Count;

        _log.LogInformation(
            "Bulk import complete. Total={T} Succeeded={S} Failed={F}",
            totalRows, succeeded, rowErrors.Count);

        return Ok(new BulkImportResult(
            TotalRows: totalRows,
            Succeeded: succeeded,
            Failed: rowErrors.Count,
            Errors: rowErrors.AsReadOnly()));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/students/template
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>Download blank Excel template for bulk import.</summary>
    [HttpGet("template")]
    [Authorize(Policy = "Students.View")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    public IActionResult DownloadTemplate()
    {
        var bytes = _excel.GenerateTemplate();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "students_import_template.xlsx");
    }

    // ─── Helpers ──────────────────────────────────────────────

    private SqlConnection Db()
        => new(_config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing."));

    /// <summary>
    /// Whitelist-based sort column sanitiser — prevents SQL injection
    /// from user-supplied sort parameters.
    /// </summary>
    private static string SanitiseSort(string? input, string fallback)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RollNumber", "FullName", "Email", "ClassName",
            "Section", "Gender", "Status", "EnrollmentDate"
        };
        return allowed.Contains(input ?? string.Empty) ? input! : fallback;
    }
}

// ─── Options ──────────────────────────────────────────────────

public sealed class SecuritySettings
{
    public const string Section = "Security";
    public int BcryptWorkFactor { get; init; } = 12;
}