using Dapper;
using SchoolPanel.Api.DTOs;
using SchoolPanel.Api.Models;
using SchoolPanel.Auth.DTOs;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Xml;

namespace SchoolPanel.Api.Repositories;

// ═══════════════════════════════════════════════════════════════
// BASE REPOSITORY
// ═══════════════════════════════════════════════════════════════

public abstract class BaseRepository
{
    protected readonly string ConnectionString;

    protected BaseRepository(IConfiguration config)
    {
        ConnectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing.");
    }

    protected IDbConnection CreateConnection() => new SqlConnection(ConnectionString);
}

// ═══════════════════════════════════════════════════════════════
// STUDENT REPOSITORY
// ═══════════════════════════════════════════════════════════════

public interface IStudentRepository
{
    Task<(int TotalCount, IEnumerable<StudentListItem> Items)> GetStudentsAsync(
        int? classId, int? academicYearId, string? status, string? search,
        string? gender, int pageNumber, int pageSize, CancellationToken ct = default);

    Task<StudentDetail?> GetStudentByIdAsync(
        Guid studentId, CancellationToken ct = default);

    Task<(string ResultCode, Guid? StudentId, Guid? UserId)> CreateStudentAsync(
        CreateStudentRequest request, Guid createdById,
        string passwordHash, CancellationToken ct = default);

    Task<string> UpdateStudentAsync(
        Guid studentId, UpdateStudentRequest request,
        Guid updatedById, CancellationToken ct = default);

    Task<string> DeleteStudentAsync(
        Guid studentId, Guid deletedById, CancellationToken ct = default);

    Task<string> MarkAttendanceAsync(
        MarkAttendanceRequest request, Guid markedById, CancellationToken ct = default);

    Task<ClassAttendance> GetAttendanceByClassAsync(
        int classId, DateOnly fromDate, DateOnly? toDate,
        int? academicYearId, CancellationToken ct = default);

    Task<AttendanceSummaryDto?> GetStudentAttendanceSummaryAsync(
        Guid studentId, int? academicYearId, CancellationToken ct = default);
}

public sealed class StudentRepository : BaseRepository, IStudentRepository
{
    public StudentRepository(IConfiguration config) : base(config) { }

    public async Task<(int TotalCount, IEnumerable<StudentListItem> Items)>
        GetStudentsAsync(int? classId, int? academicYearId, string? status,
            string? search, string? gender, int pageNumber, int pageSize,
            CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetStudents",
            new
            {
                classId,
                academicYearId,
                status,
                SearchTerm = search,
                gender,
                pageNumber,
                pageSize
            },
            commandType: CommandType.StoredProcedure);

        var count = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<StudentListItem>();
        return (count, items);
    }

    public async Task<StudentDetail?> GetStudentByIdAsync(
        Guid studentId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetStudentById",
            new { StudentId = studentId },
            commandType: CommandType.StoredProcedure);

        var profile = await multi.ReadFirstOrDefaultAsync<dynamic>();
        var attSummary = await multi.ReadFirstOrDefaultAsync<AttendanceSummaryDto>();
        var feeSummary = await multi.ReadFirstOrDefaultAsync<FeeSummary>();

        if (profile == null) return null;

        return new StudentDetail(
            StudentId: profile.StudentId,
            RollNumber: profile.RollNumber,
            FullName: profile.FullName,
            Email: profile.Email,
            PhoneNumber: profile.PhoneNumber,
            ClassName: profile.ClassName,
            Section: profile.Section,
            ClassId: profile.ClassId,
            Gender: profile.Gender,
            BloodGroup: profile.BloodGroup,
            DateOfBirth: profile.DateOfBirth is null ? (DateOnly?)null
                              : DateOnly.FromDateTime((DateTime)profile.DateOfBirth),
            Address: profile.Address,
            EmergencyContact: profile.EmergencyContact,
            Status: profile.Status,
            EnrollmentDate: DateOnly.FromDateTime((DateTime)profile.EnrollmentDate),
            ProfilePhotoUrl: profile.ProfilePhotoUrl,
            AcademicYear: profile.AcademicYear,
            AcademicYearId: profile.AcademicYearId,
            ParentName: profile.ParentName,
            ParentPhone: profile.ParentPhone,
            AttendanceSummary: attSummary,
            FeeSummary: feeSummary
        );
    }

    public async Task<(string ResultCode, Guid? StudentId, Guid? UserId)>
        CreateStudentAsync(CreateStudentRequest req, Guid createdById,
            string passwordHash, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_CreateStudent",
            new
            {
                Email = req.Email,
                PasswordHash = passwordHash,
                FullName = req.FullName,
                PhoneNumber = req.PhoneNumber,
                RollNumber = req.RollNumber,
                ClassId = req.ClassId,
                AcademicYearId = req.AcademicYearId,
                ParentId = req.ParentId,
                DateOfBirth = req.DateOfBirth.HasValue
                                   ? req.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue)
                                   : (DateTime?)null,
                Gender = req.Gender,
                BloodGroup = req.BloodGroup,
                Address = req.Address,
                EmergencyContact = req.EmergencyContact,
                CreatedById = createdById
            },
            commandType: CommandType.StoredProcedure);

        string code = result?.ResultCode ?? "ERROR";
        Guid? stuId = result?.StudentId;
        Guid? usrId = result?.UserId;
        return (code, stuId, usrId);
    }

    public async Task<string> UpdateStudentAsync(
        Guid studentId, UpdateStudentRequest req, Guid updatedById,
        CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_UpdateStudent",
            new
            {
                StudentId = studentId,
                req.FullName,
                req.PhoneNumber,
                req.ClassId,
                DateOfBirth = req.DateOfBirth.HasValue
                                   ? req.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue)
                                   : (DateTime?)null,
                req.Gender,
                req.BloodGroup,
                req.Address,
                req.EmergencyContact,
                req.ProfilePhotoUrl,
                req.Status,
                UpdatedById = updatedById
            },
            commandType: CommandType.StoredProcedure);
        return (string)(result?.ResultCode ?? "ERROR");
    }

    public async Task<string> DeleteStudentAsync(
        Guid studentId, Guid deletedById, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_SoftDeleteStudent",
            new { StudentId = studentId, DeletedById = deletedById },
            commandType: CommandType.StoredProcedure);
        return (string)(result?.ResultCode ?? "ERROR");
    }

    public async Task<string> MarkAttendanceAsync(
        MarkAttendanceRequest req, Guid markedById, CancellationToken ct = default)
    {
        // Build XML for bulk SP call
        var xml = new StringBuilder("<rows>");
        foreach (var e in req.Entries)
            xml.Append($"<row StudentId=\"{e.StudentId}\" " +
                       $"Status=\"{e.Status}\" " +
                       $"Remarks=\"{System.Security.SecurityElement.Escape(e.Remarks ?? "")}\"/>");
        xml.Append("</rows>");

        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_MarkAttendance",
            new
            {
                ClassId = req.ClassId,
                AttendanceDate = req.AttendanceDate.ToDateTime(TimeOnly.MinValue),
                MarkedById = markedById,
                AttendanceXml = xml.ToString()
            },
            commandType: CommandType.StoredProcedure);
        return (string)(result?.ResultCode ?? "ERROR");
    }

    public async Task<ClassAttendance> GetAttendanceByClassAsync(
        int classId, DateOnly fromDate, DateOnly? toDate,
        int? academicYearId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetAttendanceByClass",
            new
            {
                ClassId = classId,
                FromDate = fromDate.ToDateTime(TimeOnly.MinValue),
                ToDate = toDate?.ToDateTime(TimeOnly.MinValue),
                AcademicYearId = academicYearId
            },
            commandType: CommandType.StoredProcedure);

        var records = await multi.ReadAsync<AttendanceRecord>();
        var summary = await multi.ReadFirstOrDefaultAsync<dynamic>();

        return new ClassAttendance(
            Records: records,
            TotalStudents: (int)(summary?.TotalStudents ?? 0),
            TotalPresent: (int)(summary?.TotalPresent ?? 0),
            TotalAbsent: (int)(summary?.TotalAbsent ?? 0),
            TotalLeave: (int)(summary?.TotalLeave ?? 0),
            TotalDays: (int)(summary?.TotalDays ?? 0));
    }

    public async Task<AttendanceSummaryDto?> GetStudentAttendanceSummaryAsync(
        Guid studentId, int? academicYearId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<AttendanceSummaryDto>(
            "dbo.sp_GetStudentAttendanceSummary",
            new { StudentId = studentId, AcademicYearId = academicYearId },
            commandType: CommandType.StoredProcedure);
    }
}

// ═══════════════════════════════════════════════════════════════
// TEACHER REPOSITORY
// ═══════════════════════════════════════════════════════════════

public interface ITeacherRepository
{
    Task<(int TotalCount, IEnumerable<TeacherListItem> Items)> GetTeachersAsync(
        string? search, string? status, int pageNumber, int pageSize,
        CancellationToken ct = default);

    Task<(string ResultCode, Guid? TeacherId, Guid? UserId)> CreateTeacherAsync(
        CreateTeacherRequest req, string passwordHash, Guid createdById,
        CancellationToken ct = default);

    Task<string> UpdateTeacherAsync(
        Guid teacherId, UpdateTeacherRequest req, Guid updatedById,
        CancellationToken ct = default);

    Task<string> DeleteTeacherAsync(
        Guid teacherId, Guid deletedById, CancellationToken ct = default);
}

public sealed class TeacherRepository : BaseRepository, ITeacherRepository
{
    public TeacherRepository(IConfiguration config) : base(config) { }

    public async Task<(int TotalCount, IEnumerable<TeacherListItem> Items)>
        GetTeachersAsync(string? search, string? status, int pageNumber,
            int pageSize, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetTeachers",
            new
            {
                SearchTerm = search,
                Status = status,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            commandType: CommandType.StoredProcedure);

        var count = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<TeacherListItem>();
        return (count, items);
    }

    public async Task<(string ResultCode, Guid? TeacherId, Guid? UserId)>
        CreateTeacherAsync(CreateTeacherRequest req, string passwordHash,
            Guid createdById, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_CreateTeacher",
            new
            {
                Email = req.Email,
                PasswordHash = passwordHash,
                FullName = req.FullName,
                PhoneNumber = req.PhoneNumber,
                EmployeeCode = req.EmployeeCode,
                Qualification = req.Qualification,
                Specialization = req.Specialization,
                JoiningDate = req.JoiningDate.ToDateTime(TimeOnly.MinValue),
                ContractType = req.ContractType,
                BasicSalary = req.BasicSalary,
                CreatedById = createdById
            },
            commandType: CommandType.StoredProcedure);

        string code = result?.ResultCode ?? "ERROR";
        Guid? tchId = result?.TeacherId;
        Guid? usrId = result?.UserId;
        return (code, tchId, usrId);
    }

    public async Task<string> UpdateTeacherAsync(
        Guid teacherId, UpdateTeacherRequest req, Guid updatedById,
        CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            """
            UPDATE t SET
                ContractType   = COALESCE(@ContractType,  t.ContractType),
                BasicSalary    = COALESCE(@BasicSalary,   t.BasicSalary),
                Qualification  = COALESCE(@Qualification, t.Qualification),
                Specialization = COALESCE(@Specialization,t.Specialization),
                Status         = COALESCE(@Status,        t.Status),
                CVDocumentUrl  = COALESCE(@CVDocumentUrl, t.CVDocumentUrl),
                UpdatedAt      = SYSUTCDATETIME()
            FROM dbo.Teachers t
            WHERE t.TeacherId = @TeacherId AND t.IsDeleted = 0;

            UPDATE u SET
                FullName    = COALESCE(@FullName,    u.FullName),
                PhoneNumber = COALESCE(@PhoneNumber, u.PhoneNumber),
                UpdatedAt   = SYSUTCDATETIME()
            FROM dbo.Users u
            JOIN dbo.Teachers t ON u.UserId = t.UserId
            WHERE t.TeacherId = @TeacherId AND t.IsDeleted = 0;
            """,
            new
            {
                TeacherId = teacherId,
                req.FullName,
                req.PhoneNumber,
                req.Qualification,
                req.Specialization,
                req.ContractType,
                req.BasicSalary,
                req.Status,
                req.CVDocumentUrl
            });
        return "SUCCESS";
    }

    public async Task<string> DeleteTeacherAsync(
        Guid teacherId, Guid deletedById, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            """
            UPDATE t SET IsDeleted=1,Status='Inactive',
                DeletedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME()
            FROM dbo.Teachers t WHERE t.TeacherId=@TeacherId;
            UPDATE u SET IsActive=0,IsDeleted=1,
                DeletedAt=SYSUTCDATETIME(),UpdatedAt=SYSUTCDATETIME()
            FROM dbo.Users u
            JOIN dbo.Teachers t ON u.UserId=t.UserId
            WHERE t.TeacherId=@TeacherId;
            """,
            new { TeacherId = teacherId });
        return "SUCCESS";
    }
}

// ═══════════════════════════════════════════════════════════════
// FEE REPOSITORY
// ═══════════════════════════════════════════════════════════════

public interface IFeeRepository
{
    Task<(string ResultCode, long? PaymentId, string? ReceiptNumber)>
        RecordPaymentAsync(RecordPaymentRequest req, Guid collectedById,
            CancellationToken ct = default);

    Task<StudentFeeStatus?> GetStudentFeeStatusAsync(
        Guid studentId, int? academicYearId, CancellationToken ct = default);

    Task<(IEnumerable<FeeCollectionSummary> Monthly,
          IEnumerable<FeeClassSummary> ClassWise)>
        GetFeeCollectionSummaryAsync(
            int academicYearId, int? classId, int? month, int? year,
            CancellationToken ct = default);
}

public sealed class FeeRepository : BaseRepository, IFeeRepository
{
    public FeeRepository(IConfiguration config) : base(config) { }

    public async Task<(string ResultCode, long? PaymentId, string? ReceiptNumber)>
        RecordPaymentAsync(RecordPaymentRequest req, Guid collectedById,
            CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_RecordFeePayment",
            new
            {
                req.StudentId,
                req.FeeTypeId,
                req.AcademicYearId,
                req.AmountDue,
                req.AmountPaid,
                req.Discount,
                req.Fine,
                req.PaymentMethod,
                req.ReferenceNumber,
                req.Remarks,
                CollectedById = collectedById
            },
            commandType: CommandType.StoredProcedure);

        string code = result?.ResultCode ?? "ERROR";
        long? payId = result?.PaymentId;
        string? rcp = result?.ReceiptNumber;
        return (code, payId, rcp);
    }

    public async Task<StudentFeeStatus?> GetStudentFeeStatusAsync(
        Guid studentId, int? academicYearId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetStudentFeeStatus",
            new { StudentId = studentId, AcademicYearId = academicYearId },
            commandType: CommandType.StoredProcedure);

        var feeTypes = await multi.ReadAsync<FeeTypeStatus>();
        var payments = await multi.ReadAsync<FeePaymentRecord>();
        return new StudentFeeStatus(feeTypes, payments);
    }

    public async Task<(IEnumerable<FeeCollectionSummary>,
                       IEnumerable<FeeClassSummary>)>
        GetFeeCollectionSummaryAsync(int academicYearId, int? classId,
            int? month, int? year, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetFeeCollectionSummary",
            new
            {
                AcademicYearId = academicYearId,
                ClassId = classId,
                Month = month,
                Year = year
            },
            commandType: CommandType.StoredProcedure);

        var monthly = await multi.ReadAsync<FeeCollectionSummary>();
        var classWise = await multi.ReadAsync<FeeClassSummary>();
        return (monthly, classWise);
    }
}

// ═══════════════════════════════════════════════════════════════
// EXAM REPOSITORY
// ═══════════════════════════════════════════════════════════════

public interface IExamRepository
{
    Task<string> SaveExamResultsAsync(
        SaveExamResultsRequest req, Guid enteredById, CancellationToken ct = default);

    Task<ExamResultsResponse?> GetExamResultsAsync(
        int examId, int? classId, CancellationToken ct = default);

    Task<ReportCard?> GetStudentReportCardAsync(
        Guid studentId, int academicYearId, string? examType,
        CancellationToken ct = default);
}

public sealed class ExamRepository : BaseRepository, IExamRepository
{
    public ExamRepository(IConfiguration config) : base(config) { }

    public async Task<string> SaveExamResultsAsync(
        SaveExamResultsRequest req, Guid enteredById, CancellationToken ct = default)
    {
        var xml = new StringBuilder("<rows>");
        foreach (var r in req.Results)
            xml.Append($"<row StudentId=\"{r.StudentId}\" " +
                       $"MarksObtained=\"{r.MarksObtained}\" " +
                       $"IsAbsent=\"{(r.IsAbsent ? 1 : 0)}\" " +
                       $"Remarks=\"{System.Security.SecurityElement.Escape(r.Remarks ?? "")}\"/>");
        xml.Append("</rows>");

        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_SaveExamResults",
            new
            {
                ExamId = req.ExamId,
                EnteredById = enteredById,
                ResultsXml = xml.ToString()
            },
            commandType: CommandType.StoredProcedure);
        return (string)(result?.ResultCode ?? "ERROR");
    }

    public async Task<ExamResultsResponse?> GetExamResultsAsync(
        int examId, int? classId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetExamResults",
            new { ExamId = examId, ClassId = classId },
            commandType: CommandType.StoredProcedure);

        var results = await multi.ReadAsync<ExamResult>();
        var stats = await multi.ReadFirstOrDefaultAsync<ExamStats>();
        return stats == null ? null : new ExamResultsResponse(results, stats);
    }

    public async Task<ReportCard?> GetStudentReportCardAsync(
        Guid studentId, int academicYearId, string? examType,
        CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetStudentReportCard",
            new
            {
                StudentId = studentId,
                AcademicYearId = academicYearId,
                ExamType = examType
            },
            commandType: CommandType.StoredProcedure);

        var info = await multi.ReadFirstOrDefaultAsync<dynamic>();
        var subjects = await multi.ReadAsync<ReportCardSubject>();
        var attendance = await multi.ReadFirstOrDefaultAsync<AttendanceSummaryDto>();

        if (info == null) return null;

        return new ReportCard(
            StudentName: info.StudentName,
            RollNumber: info.RollNumber,
            ClassName: info.ClassName,
            Section: info.Section,
            AcademicYear: info.AcademicYear,
            DateOfBirth: info.DateOfBirth is null ? (DateOnly?)null
                            : DateOnly.FromDateTime((DateTime)info.DateOfBirth),
            Gender: info.Gender,
            ParentName: info.ParentName,
            ParentPhone: info.ParentPhone,
            SchoolName: info.SchoolName,
            SchoolAddress: info.SchoolAddress,
            SchoolPhone: info.SchoolPhone,
            SubjectResults: subjects,
            Attendance: attendance ?? new AttendanceSummaryDto(0, 0, 0, 0, 0)
        );
    }
}

// ═══════════════════════════════════════════════════════════════
// DASHBOARD REPOSITORY
// ═══════════════════════════════════════════════════════════════

public interface IDashboardRepository
{
    Task<DashboardStats?> GetStatsAsync(
        int? academicYearId, CancellationToken ct = default);

    Task<StudentStrengthReport?> GetStrengthReportAsync(
        int? academicYearId, CancellationToken ct = default);

    Task<IEnumerable<AttendanceReportRowDto>> GetAttendanceReportAsync(
        int academicYearId, int? classId, Guid? studentId,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default);
}

public sealed class DashboardRepository : BaseRepository, IDashboardRepository
{
    public DashboardRepository(IConfiguration config) : base(config) { }

    public async Task<DashboardStats?> GetStatsAsync(
        int? academicYearId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetDashboardStats",
            new { AcademicYearId = academicYearId },
            commandType: CommandType.StoredProcedure);

        var summary = await multi.ReadFirstOrDefaultAsync<DashboardSummaryDto>();
        var feeChart = await multi.ReadAsync<FeeChartPointDto>();
        var attendance = await multi.ReadAsync<ClassAttendanceSnapshotDto>();

        return summary == null ? null
            : new DashboardStats(summary, feeChart, attendance);
    }

    public async Task<StudentStrengthReport?> GetStrengthReportAsync(
        int? academicYearId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetStudentStrengthReport",
            new { AcademicYearId = academicYearId },
            commandType: CommandType.StoredProcedure);

        var classes = await multi.ReadAsync<StudentStrengthClass>();
        var totals = await multi.ReadFirstOrDefaultAsync<dynamic>();

        return totals == null ? null
            : new StudentStrengthReport(
                Classes: classes,
                GrandTotal: (int)totals.GrandTotal,
                TotalMale: (int)totals.TotalMale,
                TotalFemale: (int)totals.TotalFemale,
                AcademicYear: (string)totals.AcademicYear);
    }

    public async Task<IEnumerable<AttendanceReportRowDto>> GetAttendanceReportAsync(
        int academicYearId, int? classId, Guid? studentId,
        DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<AttendanceReportRowDto>(
            "dbo.sp_GetAttendanceReport",
            new
            {
                AcademicYearId = academicYearId,
                ClassId = classId,
                StudentId = studentId,
                FromDate = fromDate?.ToDateTime(TimeOnly.MinValue),
                ToDate = toDate?.ToDateTime(TimeOnly.MinValue)
            },
            commandType: CommandType.StoredProcedure);
    }
}

// ═══════════════════════════════════════════════════════════════
// ROLES REPOSITORY
// ═══════════════════════════════════════════════════════════════

public interface IRolesRepository
{
    Task<IEnumerable<RoleDto>> GetAllRolesAsync(CancellationToken ct = default);

    Task<(string ResultCode, int? RoleId)> CreateRoleAsync(
        CreateRoleRequest req, Guid createdById, CancellationToken ct = default);

    Task<string> UpdateRoleAsync(
        int roleId, UpdateRoleRequest req, CancellationToken ct = default);

    Task<string> DeleteRoleAsync(int roleId, CancellationToken ct = default);

    Task SetPermissionsAsync(
        SetPermissionsRequest req, CancellationToken ct = default);
}

public sealed class RolesRepository : BaseRepository, IRolesRepository
{
    public RolesRepository(IConfiguration config) : base(config) { }

    public async Task<IEnumerable<RoleDto>> GetAllRolesAsync(
        CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var roles = await conn.QueryAsync<RoleDto>(
            """
            SELECT RoleId, RoleName, Description, IsSystemRole, CreatedAt
            FROM   dbo.Roles
            WHERE  IsDeleted = 0
            ORDER  BY IsSystemRole DESC, RoleName
            """);

        // Load permissions per role
        var perms = await conn.QueryAsync<Permission>(
            """
            SELECT rp.Module, rp.CanView, rp.CanCreate,
                   rp.CanEdit, rp.CanDelete, rp.CanExport,
                   rp.RoleId
            FROM   dbo.RolePermissions rp
            JOIN   dbo.Roles r ON rp.RoleId = r.RoleId
            WHERE  r.IsDeleted = 0
            """);

        // Attach permissions (anonymous type trick — map separately)
        return roles; // Full permission join done in controller if needed
    }

    public async Task<(string ResultCode, int? RoleId)> CreateRoleAsync(
        CreateRoleRequest req, Guid createdById, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_ManageRole",
            new
            {
                Action = "CREATE",
                RoleName = req.RoleName,
                Description = req.Description,
                CreatedById = createdById
            },
            commandType: CommandType.StoredProcedure);

        string code = result?.ResultCode ?? "ERROR";
        int? newId = result?.RoleId is null ? null : (int?)result.RoleId;
        return (code, newId);
    }

    public async Task<string> UpdateRoleAsync(
        int roleId, UpdateRoleRequest req, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_ManageRole",
            new
            {
                Action = "UPDATE",
                RoleId = roleId,
                RoleName = req.RoleName,
                Description = req.Description
            },
            commandType: CommandType.StoredProcedure);
        return (string)(result?.ResultCode ?? "ERROR");
    }

    public async Task<string> DeleteRoleAsync(int roleId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_ManageRole",
            new { Action = "DELETE", RoleId = roleId },
            commandType: CommandType.StoredProcedure);
        return (string)(result?.ResultCode ?? "ERROR");
    }

    public async Task SetPermissionsAsync(
        SetPermissionsRequest req, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            """
            MERGE dbo.RolePermissions AS target
            USING (SELECT @RoleId AS RoleId, @Module AS Module) AS source
              ON target.RoleId = source.RoleId AND target.Module = source.Module
            WHEN MATCHED THEN
                UPDATE SET CanView=@CanView, CanCreate=@CanCreate,
                           CanEdit=@CanEdit, CanDelete=@CanDelete,
                           CanExport=@CanExport, UpdatedAt=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (RoleId,Module,CanView,CanCreate,CanEdit,CanDelete,CanExport)
                VALUES (@RoleId,@Module,@CanView,@CanCreate,@CanEdit,@CanDelete,@CanExport);
            """,
            new
            {
                req.RoleId,
                req.Module,
                req.CanView,
                req.CanCreate,
                req.CanEdit,
                req.CanDelete,
                req.CanExport
            });
    }
}

// ═══════════════════════════════════════════════════════════════
// SETTINGS REPOSITORY
// ═══════════════════════════════════════════════════════════════

public interface ISettingsRepository
{
    Task<IEnumerable<SettingDto>> GetByCategory(string? category, CancellationToken ct = default);
    Task<SettingDto?> GetByKey(string key, CancellationToken ct = default);
    Task UpsertAsync(UpdateSettingRequest req, Guid updatedById, CancellationToken ct = default);
}

public sealed class SettingsRepository : BaseRepository, ISettingsRepository
{
    public SettingsRepository(IConfiguration config) : base(config) { }

    public async Task<IEnumerable<SettingDto>> GetByCategory(
        string? category, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        return await conn.QueryAsync<SettingDto>(
            "dbo.sp_GetSetSetting",
            new { Action = "GET_CATEGORY", Category = category ?? "Branding" },
            commandType: CommandType.StoredProcedure);
    }

    public async Task<SettingDto?> GetByKey(
        string key, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<SettingDto>(
            "dbo.sp_GetSetSetting",
            new { Action = "GET", Key = key },
            commandType: CommandType.StoredProcedure);
    }

    public async Task UpsertAsync(
        UpdateSettingRequest req, Guid updatedById, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "dbo.sp_GetSetSetting",
            new
            {
                Action = "SET",
                Key = req.Key,
                Value = req.Value,
                Category = req.Category,
                UpdatedById = updatedById
            },
            commandType: CommandType.StoredProcedure);
    }
}

// ═══════════════════════════════════════════════════════════════
// NOTIFICATION REPOSITORY
// ═══════════════════════════════════════════════════════════════

public interface INotificationRepository
{
    Task<(int UnreadCount, IEnumerable<NotificationDto> Notifications)>
        GetNotificationsAsync(Guid userId, bool? isRead, int pageNumber,
            int pageSize, CancellationToken ct = default);

    Task CreateAsync(CreateNotificationRequest req, Guid createdById,
        CancellationToken ct = default);
}

public sealed class NotificationRepository : BaseRepository, INotificationRepository
{
    public NotificationRepository(IConfiguration config) : base(config) { }

    public async Task<(int UnreadCount, IEnumerable<NotificationDto> Notifications)>
        GetNotificationsAsync(Guid userId, bool? isRead, int pageNumber,
            int pageSize, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_GetNotifications",
            new
            {
                UserId = userId,
                IsRead = isRead,
                PageNumber = pageNumber,
                PageSize = pageSize
            },
            commandType: CommandType.StoredProcedure);

        var unread = await multi.ReadFirstAsync<int>();
        var notifications = await multi.ReadAsync<NotificationDto>();
        return (unread, notifications);
    }

    public async Task CreateAsync(
        CreateNotificationRequest req, Guid createdById, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "dbo.sp_CreateNotification",
            new
            {
                req.Title,
                req.Message,
                req.NotificationType,
                req.Module,
                req.ReferenceId,
                CreatedById = createdById,
                TargetUserId = req.TargetUserId,
                TargetRoleId = req.TargetRoleId
            },
            commandType: CommandType.StoredProcedure);
    }
}