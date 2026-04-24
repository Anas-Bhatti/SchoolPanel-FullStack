// ============================================================
// DTOs/SharedDtos.cs
// Pagination wrappers, sort/filter contracts, ProblemDetails
// factory, and shared value types used across all controllers.
// ============================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace SchoolPanel.Controllers.DTOs;

// ═══════════════════════════════════════════════════════════════
// PAGINATION
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Standard pagination query parameters.
/// Bound from query string: ?page=1&pageSize=20&search=ali&sort=name&dir=asc
/// </summary>
public record PaginationQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 200)]
    public int PageSize { get; init; } = 20;

    [MaxLength(100)]
    public string? Search { get; init; }

    [MaxLength(50)]
    public string? Sort { get; init; }

    /// <summary>asc or desc</summary>
    public string Dir { get; init; } = "asc";

    public int Offset => (Page - 1) * PageSize;

    public bool IsDescending =>
        string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Paginated response envelope returned by all list endpoints.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage
)
{
    public static PagedResult<T> From(
        IEnumerable<T> items,
        int totalCount,
        int page,
        int pageSize)
    {
        var list = items as IReadOnlyList<T> ?? items.ToList().AsReadOnly();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PagedResult<T>(
            Items: list,
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize,
            TotalPages: totalPages,
            HasNextPage: page < totalPages,
            HasPreviousPage: page > 1);
    }
}

// ═══════════════════════════════════════════════════════════════
// API ENVELOPE
// ═══════════════════════════════════════════════════════════════

public sealed record ApiOk<T>(T Data, string? Message = null);

// ═══════════════════════════════════════════════════════════════
// PROBLEM DETAILS FACTORY
// Centralises RFC 7807 error responses so every controller
// returns the same shape for every error type.
// ═══════════════════════════════════════════════════════════════

public static class Problem
{
    public static ProblemDetails NotFound(string detail, string? instance = null) =>
        Build(404, "NOT_FOUND", "Resource not found.", detail, instance);

    public static ProblemDetails BadRequest(string detail, string? instance = null) =>
        Build(400, "BAD_REQUEST", "Invalid request.", detail, instance);

    public static ProblemDetails Conflict(string detail, string? instance = null) =>
        Build(409, "CONFLICT", "Conflict.", detail, instance);

    public static ProblemDetails Forbidden(string detail, string? instance = null) =>
        Build(403, "FORBIDDEN", "Access denied.", detail, instance);

    public static ProblemDetails Unprocessable(string detail, string? instance = null) =>
        Build(422, "UNPROCESSABLE", "Unprocessable entity.", detail, instance);

    public static ProblemDetails ServerError(string detail = "An unexpected error occurred.",
        string? instance = null) =>
        Build(500, "SERVER_ERROR", "Internal server error.", detail, instance);

    public static ProblemDetails Custom(int status, string code,
        string title, string detail, string? instance = null) =>
        Build(status, code, title, detail, instance);

    private static ProblemDetails Build(
        int status, string code, string title, string detail, string? instance)
    {
        var p = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = instance
        };
        p.Extensions["code"] = code;
        return p;
    }
}

// ═══════════════════════════════════════════════════════════════
// STUDENT DTOs
// ═══════════════════════════════════════════════════════════════

public record StudentFilters : PaginationQuery
{
    public int? ClassId { get; init; }
    public string? Section { get; init; }
    public string? Status { get; init; }
    public string? Gender { get; init; }
    public int? AcademicYearId { get; init; }
}

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
    string AcademicYear,
    DateOnly EnrollmentDate,
    string? ProfilePhotoUrl
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
    AttendanceSummary Attendance,
    FeesSummary Fees
);

public sealed record AttendanceSummary(
    int TotalDays,
    int PresentDays,
    int AbsentDays,
    int LeaveDays,
    decimal AttendancePct
);

public sealed record FeesSummary(
    decimal TotalDue,
    decimal TotalPaid,
    decimal BalanceDue
);

public sealed record CreateStudentRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; init; } = string.Empty;

    [MaxLength(20)]
    public string? PhoneNumber { get; init; }

    [Required, MaxLength(50)]
    public string RollNumber { get; init; } = string.Empty;

    [Required]
    public int ClassId { get; init; }

    [Required]
    public int AcademicYearId { get; init; }

    public Guid? ParentId { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    [MaxLength(10)]
    public string Gender { get; init; } = "Male";

    [MaxLength(5)]
    public string? BloodGroup { get; init; }

    [MaxLength(500)]
    public string? Address { get; init; }

    [MaxLength(20)]
    public string? EmergencyContact { get; init; }
}

public sealed record UpdateStudentRequest
{
    [MaxLength(150)] public string? FullName { get; init; }
    [MaxLength(20)] public string? PhoneNumber { get; init; }
    public int? ClassId { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    [MaxLength(10)] public string? Gender { get; init; }
    [MaxLength(5)] public string? BloodGroup { get; init; }
    [MaxLength(500)] public string? Address { get; init; }
    [MaxLength(20)] public string? EmergencyContact { get; init; }
    [MaxLength(20)] public string? Status { get; init; }
}

public sealed record BulkImportResult(
    int TotalRows,
    int Succeeded,
    int Failed,
    IReadOnlyList<BulkRowError> Errors
);

public sealed record BulkRowError(int Row, string RollNumber, string Reason);

// ═══════════════════════════════════════════════════════════════
// ATTENDANCE DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record AttendanceEntry(
    [Required] Guid StudentId,
    [Required, StringLength(1)] string Status,  // P A L H
    [MaxLength(200)] string? Remarks = null
);

public sealed record MarkAttendanceRequest(
    [Required] int ClassId,
    [Required] DateOnly AttendanceDate,
    [Required, MinLength(1)]
    IReadOnlyList<AttendanceEntry>       Entries
);

public sealed record MonthlyAttendanceQuery
{
    public Guid? StudentId { get; init; }
    public int? ClassId { get; init; }
    public int Month { get; init; } = DateTime.UtcNow.Month;
    public int Year { get; init; } = DateTime.UtcNow.Year;
    public int? AcademicYearId { get; init; }
}

public sealed record AttendanceRow(
    Guid StudentId,
    string RollNumber,
    string FullName,
    DateOnly AttendanceDate,
    string Status,
    string? Remarks,
    DateTime? MarkedAt,
    string? MarkedBy
);

public sealed record AttendanceDay(
    DateOnly Date,
    string DayName,
    int TotalStudents,
    int Present,
    int Absent,
    int Leave,
    int Holiday,
    decimal PresentPct
);

public sealed record TodayAttendance(
    DateOnly Date,
    int TotalStudents,
    int TotalTeachers,
    int StudentsPresent,
    int StudentsAbsent,
    int StudentsLeave,
    decimal StudentPresentPct,
    IReadOnlyList<ClassAttendanceSnapshot> ClassBreakdown
);

public sealed record ClassAttendanceSnapshot(
    string ClassName,
    string Section,
    int Total,
    int Present,
    int Absent,
    int Leave
);

// ═══════════════════════════════════════════════════════════════
// FEES DTOs
// ═══════════════════════════════════════════════════════════════

public sealed record FeesDue(
    Guid StudentId,
    string StudentName,
    string RollNumber,
    string ClassName,
    IReadOnlyList<FeeLineItem> LineItems,
    decimal GrandTotalDue,
    decimal GrandTotalPaid,
    decimal GrandBalance
);

public sealed record FeeLineItem(
    int FeeTypeId,
    string FeeTypeName,
    string Frequency,
    decimal AmountDue,
    decimal AmountPaid,
    decimal Discount,
    decimal Fine,
    decimal Balance,
    DateOnly? DueDate,
    bool IsOverdue
);

public sealed record RecordPaymentRequest
{
    [Required]
    public Guid StudentId { get; init; }

    [Required]
    public int FeeTypeId { get; init; }

    [Required]
    public int AcademicYearId { get; init; }

    [Required, Range(0.01, 10_000_000)]
    public decimal AmountDue { get; init; }

    [Required, Range(0.01, 10_000_000)]
    public decimal AmountPaid { get; init; }

    [Range(0, 10_000_000)]
    public decimal Discount { get; init; } = 0;

    [Range(0, 10_000_000)]
    public decimal Fine { get; init; } = 0;

    [MaxLength(50)]
    public string PaymentMethod { get; init; } = "Cash";

    [MaxLength(100)]
    public string? ReferenceNumber { get; init; }

    [MaxLength(500)]
    public string? Remarks { get; init; }
}

public sealed record PaymentReceipt(
    long PaymentId,
    string ReceiptNumber,
    string StudentName,
    string RollNumber,
    string ClassName,
    string FeeTypeName,
    decimal AmountDue,
    decimal AmountPaid,
    decimal Discount,
    decimal Fine,
    decimal Balance,
    string PaymentMethod,
    string? ReferenceNumber,
    DateOnly PaymentDate,
    string CollectedBy,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone
);

public sealed record FeesSummaryQuery
{
    [Required]
    public int AcademicYearId { get; init; }
    public int? ClassId { get; init; }
    public int? Month { get; init; }
    public int? Year { get; init; }
}

public sealed record FeeCollectionSummary(
    IReadOnlyList<MonthlyFee> Monthly,
    IReadOnlyList<ClassFee> ByClass,
    decimal TotalDue,
    decimal TotalCollected,
    decimal TotalPending
);

public sealed record MonthlyFee(
    int Year,
    int Month,
    string MonthName,
    int StudentsWhoPayd,
    decimal TotalDue,
    decimal TotalCollected,
    decimal TotalPending,
    decimal TotalDiscount,
    decimal TotalFine
);

public sealed record ClassFee(
    string ClassName,
    string Section,
    int TotalStudents,
    decimal TotalDue,
    decimal TotalCollected,
    decimal TotalPending
);