// ============================================================
// Models/ReportModels.cs
// Strongly-typed models that map 1:1 to stored procedure
// result sets. No dynamic objects past the repository layer.
// ============================================================

namespace SchoolPanel.Reports.Models;

// ─── School settings (loaded once per request) ────────────────

public sealed class SchoolSettings
{
    public string Name { get; init; } = "School Management Panel";
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public string? LogoUrl { get; init; }  // Azure Blob URL
    public string PrimaryColor { get; init; } = "#2563EB";
    public string Currency { get; init; } = "Rs.";
    public string DateFormat { get; init; } = "dd MMM yyyy";
}

// ─── Report Card ──────────────────────────────────────────────

public sealed class ReportCardHeader
{
    public string StudentName { get; init; } = string.Empty;
    public string RollNumber { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public string AcademicYear { get; init; } = string.Empty;
    public string? DateOfBirth { get; init; }
    public string Gender { get; init; } = string.Empty;
    public string? ParentName { get; init; }
    public string? ParentPhone { get; init; }
    public string? ProfilePhotoUrl { get; init; }
    // School fields (cross-joined from Settings)
    public string SchoolName { get; init; } = string.Empty;
    public string? SchoolAddress { get; init; }
    public string? SchoolPhone { get; init; }
}

public sealed class ReportCardSubjectRow
{
    public string SubjectName { get; init; } = string.Empty;
    public string ExamName { get; init; } = string.Empty;
    public string ExamType { get; init; } = string.Empty;
    public decimal TotalMarks { get; init; }
    public decimal PassMarks { get; init; }
    public decimal MarksObtained { get; init; }
    public string? Grade { get; init; }
    public bool IsAbsent { get; init; }
    public decimal Percentage { get; init; }
}

public sealed class ReportCardAttendance
{
    public int TotalDays { get; init; }
    public int PresentDays { get; init; }
    public int AbsentDays { get; init; }
    public int LeaveDays { get; init; }
    public decimal AttendancePct { get; init; }
}

public sealed class ReportCardData
{
    public ReportCardHeader Header { get; init; } = new();
    public IReadOnlyList<ReportCardSubjectRow> Subjects { get; init; } = [];
    public ReportCardAttendance Attendance { get; init; } = new();
    public SchoolSettings School { get; init; } = new();
}

// ─── Fee Receipt ──────────────────────────────────────────────

public sealed class FeeReceiptData
{
    public long PaymentId { get; init; }
    public string ReceiptNumber { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public string RollNumber { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string FeeTypeName { get; init; } = string.Empty;
    public decimal AmountDue { get; init; }
    public decimal AmountPaid { get; init; }
    public decimal Discount { get; init; }
    public decimal Fine { get; init; }
    public decimal BalanceDue { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public string? ReferenceNumber { get; init; }
    public DateTime PaymentDate { get; init; }
    public string CollectedBy { get; init; } = string.Empty;
    public SchoolSettings School { get; init; } = new();
}

// ─── Attendance Sheet (Excel) ─────────────────────────────────

public sealed class AttendanceSheetHeader
{
    public string ClassName { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public string ClassTeacher { get; init; } = string.Empty;
    public int Month { get; init; }
    public int Year { get; init; }
    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    public int WorkingDays { get; init; }
}

public sealed class AttendanceStudentRow
{
    public string RollNumber { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    // Key = day number (1-31), Value = "P"|"A"|"L"|"H"|""
    public Dictionary<int, string> DailyStatus { get; init; } = [];
    public int PresentCount { get; init; }
    public int AbsentCount { get; init; }
    public int LeaveCount { get; init; }
    public decimal AttendancePct { get; init; }
}

public sealed class AttendanceSheetData
{
    public AttendanceSheetHeader Header { get; init; } = new();
    public IReadOnlyList<AttendanceStudentRow> Rows { get; init; } = [];
    public SchoolSettings School { get; init; } = new();
}

// ─── Exam Result Sheet ────────────────────────────────────────

public sealed class ExamResultHeader
{
    public string ExamName { get; init; } = string.Empty;
    public string ExamType { get; init; } = string.Empty;
    public string SubjectName { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public string Section { get; init; } = string.Empty;
    public DateTime ExamDate { get; init; }
    public decimal TotalMarks { get; init; }
    public decimal PassMarks { get; init; }
    public string AcademicYear { get; init; } = string.Empty;
}

public sealed class ExamResultStudentRow
{
    public string RollNumber { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public decimal MarksObtained { get; init; }
    public string? Grade { get; init; }
    public bool IsAbsent { get; init; }
    public decimal Percentage { get; init; }
    public bool Passed { get; init; }
}

public sealed class ExamResultStats
{
    public int TotalStudents { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public int Absent { get; init; }
    public decimal AverageMarks { get; init; }
    public decimal HighestMarks { get; init; }
    public decimal LowestMarks { get; init; }
    public decimal PassPct => TotalStudents == 0 ? 0
        : Math.Round(Passed * 100m / TotalStudents, 1);
}

public sealed class ExamResultSheetData
{
    public ExamResultHeader Header { get; init; } = new();
    public IReadOnlyList<ExamResultStudentRow> Rows { get; init; } = [];
    public ExamResultStats Stats { get; init; } = new();
    public SchoolSettings School { get; init; } = new();
}