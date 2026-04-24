using SchoolPanel.Auth.DTOs;
using System.ComponentModel.DataAnnotations;

namespace SchoolPanel.Api.DTOs;

// ═══════════════════════════════════════════════════════════════
// STUDENT DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record CreateStudentRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MaxLength(150)] string FullName,
    [MaxLength(20)] string? PhoneNumber,
    [Required, MaxLength(50)] string RollNumber,
    [Required] int ClassId,
    [Required] int AcademicYearId,
                                               Guid? ParentId,
                                               DateOnly? DateOfBirth,
    [MaxLength(10)] string Gender = "Male",
    [MaxLength(5)] string? BloodGroup = null,
    [MaxLength(500)] string? Address = null,
    [MaxLength(20)] string? EmergencyContact = null
);

public sealed record UpdateStudentRequest(
    [MaxLength(150)] string? FullName = null,
    [MaxLength(20)] string? PhoneNumber = null,
                      int? ClassId = null,
                      DateOnly? DateOfBirth = null,
    [MaxLength(10)] string? Gender = null,
    [MaxLength(5)] string? BloodGroup = null,
    [MaxLength(500)] string? Address = null,
    [MaxLength(20)] string? EmergencyContact = null,
    [MaxLength(1000)] string? ProfilePhotoUrl = null,
    [MaxLength(20)] string? Status = null
);

public sealed record StudentListItem(
    Guid StudentId,
    string RollNumber,
    string FullName,
    string Email,
    string? PhoneNumber,
    string ClassName,
    string Section,
    string Gender,
    string Status,
    DateOnly EnrollmentDate,
    string? ProfilePhotoUrl,
    string AcademicYear
);

public sealed record StudentDetail(
    Guid StudentId,
    string RollNumber,
    string FullName,
    string Email,
    string? PhoneNumber,
    string ClassName,
    string Section,
    int ClassId,
    string Gender,
    string? BloodGroup,
    DateOnly? DateOfBirth,
    string? Address,
    string? EmergencyContact,
    string Status,
    DateOnly EnrollmentDate,
    string? ProfilePhotoUrl,
    string AcademicYear,
    int AcademicYearId,
    string? ParentName,
    string? ParentPhone,
    // Summaries
    AttendanceSummaryDto? AttendanceSummary,
    FeeSummary? FeeSummary
);

// ═══════════════════════════════════════════════════════════════
// TEACHER DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record CreateTeacherRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [Required, MaxLength(150)] string FullName,
    [MaxLength(20)] string? PhoneNumber,
    [Required, MaxLength(50)] string EmployeeCode,
    [MaxLength(200)] string? Qualification,
    [MaxLength(200)] string? Specialization,
    [Required] DateOnly JoiningDate,
    [MaxLength(50)] string ContractType = "Permanent",
    [Range(0, 10_000_000)] decimal BasicSalary = 0
);

public sealed record UpdateTeacherRequest(
    [MaxLength(150)] string? FullName = null,
    [MaxLength(20)] string? PhoneNumber = null,
    [MaxLength(200)] string? Qualification = null,
    [MaxLength(200)] string? Specialization = null,
    [MaxLength(50)] string? ContractType = null,
    [Range(0, 10_000_000)] decimal? BasicSalary = null,
    [MaxLength(20)] string? Status = null,
    [MaxLength(1000)] string? CVDocumentUrl = null
);

public sealed record TeacherListItem(
    Guid TeacherId,
    string EmployeeCode,
    string FullName,
    string Email,
    string? PhoneNumber,
    string? Specialization,
    string ContractType,
    string Status,
    DateOnly JoiningDate,
    decimal BasicSalary,
    string? ProfilePhotoUrl,
    string? ClassTeacherOf,
    string? Section
);

// ═══════════════════════════════════════════════════════════════
// ATTENDANCE DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record AttendanceEntryRequest(
    [Required] Guid StudentId,
    [Required, StringLength(1)] string Status,   // P, A, L, H
    [MaxLength(200)] string? Remarks = null
);

public sealed record MarkAttendanceRequest(
    [Required] int ClassId,
    [Required] DateOnly AttendanceDate,
    [Required, MinLength(1)] List<AttendanceEntryRequest> Entries
);

public sealed record AttendanceSummaryDto(
    int TotalDays,
    int PresentDays,
    int AbsentDays,
    int LeaveDays,
    decimal AttendancePercentage
);

public sealed record AttendanceRecord(
    Guid StudentId,
    string RollNumber,
    string FullName,
    DateOnly AttendanceDate,
    string? Status,
    string? Remarks,
    DateTime? MarkedAt,
    string? MarkedBy
);

public sealed record ClassAttendance(
    IEnumerable<AttendanceRecord> Records,
    int TotalStudents,
    int TotalPresent,
    int TotalAbsent,
    int TotalLeave,
    int TotalDays
);

// ═══════════════════════════════════════════════════════════════
// FEE DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record RecordPaymentRequest(
    [Required] Guid StudentId,
    [Required] int FeeTypeId,
    [Required] int AcademicYearId,
    [Required, Range(0, 10_000_000)] decimal AmountDue,
    [Required, Range(0, 10_000_000)] decimal AmountPaid,
    [Range(0, 10_000_000)] decimal Discount = 0,
    [Range(0, 10_000_000)] decimal Fine = 0,
    [MaxLength(50)] string PaymentMethod = "Cash",
    [MaxLength(100)] string? ReferenceNumber = null,
    [MaxLength(500)] string? Remarks = null
);

public sealed record FeePaymentRecord(
    long PaymentId,
    string ReceiptNumber,
    string FeeTypeName,
    decimal AmountDue,
    decimal AmountPaid,
    decimal Discount,
    decimal Fine,
    decimal BalanceDue,
    string PaymentMethod,
    DateOnly PaymentDate,
    string? ReferenceNumber,
    string CollectedBy
);

public sealed record FeeTypeStatus(
    int FeeTypeId,
    string FeeTypeName,
    decimal FeeAmount,
    string Frequency,
    DateOnly? DueDate,
    decimal TotalPaid,
    decimal TotalDiscount,
    decimal TotalFine,
    decimal BalanceDue,
    int PaymentCount
);

public sealed record StudentFeeStatus(
    IEnumerable<FeeTypeStatus> FeeTypes,
    IEnumerable<FeePaymentRecord> Payments
);

public sealed record FeeSummary(
    decimal TotalDue,
    decimal TotalPaid,
    decimal TotalBalance
);

public sealed record FeeCollectionSummary(
    int PaymentYear,
    int PaymentMonth,
    string MonthName,
    int StudentsWhoPaid,
    decimal TotalDue,
    decimal TotalCollected,
    decimal TotalPending,
    decimal TotalDiscount,
    decimal TotalFine
);

public sealed record FeeClassSummary(
    string ClassName,
    string Section,
    int TotalStudents,
    decimal TotalDue,
    decimal TotalCollected,
    decimal TotalPending
);

// ═══════════════════════════════════════════════════════════════
// EXAM DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record CreateExamRequest(
    [Required, MaxLength(200)] string ExamName,
    [Required] int ClassId,
    [Required] int SubjectId,
    [Required] int AcademicYearId,
    [Required] DateOnly ExamDate,
    [MaxLength(50)] string ExamType = "Annual",
    [Range(1, 1000)] decimal TotalMarks = 100,
    [Range(1, 1000)] decimal PassMarks = 40,
    [Range(1, 480)] int? Duration = null
);

public sealed record ExamResultEntryRequest(
    [Required] Guid StudentId,
    [Range(0, 1000)] decimal MarksObtained = 0,
    bool IsAbsent = false,
    [MaxLength(500)] string? Remarks = null
);

public sealed record SaveExamResultsRequest(
    [Required] int ExamId,
    [Required, MinLength(1)] List<ExamResultEntryRequest> Results
);

public sealed record ExamResult(
    long ResultId,
    string RollNumber,
    string StudentName,
    decimal MarksObtained,
    decimal TotalMarks,
    decimal PassMarks,
    string? Grade,
    bool IsAbsent,
    string? Remarks,
    decimal Percentage
);

public sealed record ExamStats(
    int TotalStudents,
    int Passed,
    int Failed,
    int Absent,
    decimal AverageMarks,
    decimal HighestMarks,
    decimal LowestMarks
);

public sealed record ExamResultsResponse(
    IEnumerable<ExamResult> Results,
    ExamStats Stats
);

// ═══════════════════════════════════════════════════════════════
// REPORT DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record ReportCardSubject(
    string SubjectName,
    string ExamName,
    string ExamType,
    DateOnly ExamDate,
    decimal TotalMarks,
    decimal PassMarks,
    decimal MarksObtained,
    string? Grade,
    bool IsAbsent,
    decimal Percentage
);

public sealed record ReportCard(
    string StudentName,
    string RollNumber,
    string ClassName,
    string Section,
    string AcademicYear,
    DateOnly? DateOfBirth,
    string Gender,
    string? ParentName,
    string? ParentPhone,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    IEnumerable<ReportCardSubject> SubjectResults,
    AttendanceSummaryDto Attendance
);

public sealed record StudentStrengthClass(
    string ClassName,
    string Section,
    int Capacity,
    int TotalEnrolled,
    int MaleCount,
    int FemaleCount,
    int OtherCount,
    int ActiveCount,
    int VacantSeats,
    string? ClassTeacher
);

public sealed record StudentStrengthReport(
    IEnumerable<StudentStrengthClass> Classes,
    int GrandTotal,
    int TotalMale,
    int TotalFemale,
    string AcademicYear
);

public sealed record AttendanceReportRowDto(
    string StudentName,
    string RollNumber,
    string ClassName,
    string Section,
    int TotalDays,
    int Present,
    int Absent,
    int Leave,
    decimal AttendancePercentage
);

// ═══════════════════════════════════════════════════════════════
// DASHBOARD DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record DashboardSummaryDto(
    int TotalStudents,
    int TotalTeachers,
    int TotalClasses,
    decimal FeeCollectedThisMonth,
    decimal TotalFeePending,
    int TotalMaleStudents,
    int TotalFemaleStudents
);

public sealed record FeeChartPointDto(
    int PayYear,
    int PayMonth,
    string MonthLabel,
    decimal Collected,
    decimal Pending
);

public sealed record ClassAttendanceSnapshotDto(
    string ClassName,
    string Section,
    int TotalStudents,
    int Present,
    int Absent,
    int Leave
);

public sealed record DashboardStats(
    DashboardSummaryDto Summary,
    IEnumerable<FeeChartPointDto> FeeChart,
    IEnumerable<ClassAttendanceSnapshotDto> AttendanceSnapshot
);

// ═══════════════════════════════════════════════════════════════
// ROLE + PERMISSION DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record CreateRoleRequest(
    [Required, MaxLength(100)] string RoleName,
    [MaxLength(500)] string? Description = null
);

public sealed record UpdateRoleRequest(
    [MaxLength(100)] string? RoleName = null,
    [MaxLength(500)] string? Description = null
);

public sealed record SetPermissionsRequest(
    [Required] int RoleId,
    [Required] string Module,
    bool CanView = false,
    bool CanCreate = false,
    bool CanEdit = false,
    bool CanDelete = false,
    bool CanExport = false
);

public sealed record RoleDto(
    int RoleId,
    string RoleName,
    string? Description,
    bool IsSystemRole,
    DateTime CreatedAt,
    IEnumerable<Permission>? Permissions = null
);

// ═══════════════════════════════════════════════════════════════
// SETTINGS DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record SettingDto(
    string SettingKey,
    string? SettingValue,
    string Category,
    string? Description
);

public sealed record UpdateSettingRequest(
    [Required, MaxLength(200)] string Key,
    [MaxLength(4000)] string? Value,
    [MaxLength(100)] string? Category = null
);

// ═══════════════════════════════════════════════════════════════
// NOTIFICATION DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record NotificationDto(
    long NotificationId,
    string Title,
    string Message,
    string NotificationType,
    string? Module,
    string? ReferenceId,
    bool IsRead,
    DateTime? ReadAt,
    DateTime CreatedAt
);

public sealed record CreateNotificationRequest(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(2000)] string Message,
    [MaxLength(50)] string NotificationType = "Info",
    [MaxLength(100)] string? Module = null,
    [MaxLength(100)] string? ReferenceId = null,
                                Guid? TargetUserId = null,
                                int? TargetRoleId = null
);

// ═══════════════════════════════════════════════════════════════
// AUDIT LOG DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record AuditLogDto(
    long LogId,
    Guid? UserId,
    string? UserEmail,
    string Action,
    string Module,
    string? RecordId,
    string? Description,
    string IPAddress,
    bool IsSuccess,
    string? ErrorMessage,
    DateTime Timestamp
);

// ═══════════════════════════════════════════════════════════════
// FILE UPLOAD DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record FileUploadResult(
    bool Success,
    string? Url,
    string? BlobName,
    string? Error
);