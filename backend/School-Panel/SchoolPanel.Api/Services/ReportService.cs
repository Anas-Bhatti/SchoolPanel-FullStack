// ============================================================
// Services/ReportService.cs
// Complete report generation service.
// PDF reports: QuestPDF (strongly-typed, fast, branded)
// Excel reports: ClosedXML (full styling, freeze panes, etc.)
// ============================================================

using System.Drawing;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolPanel.Reports.Helpers;
using SchoolPanel.Reports.Models;

namespace SchoolPanel.Reports.Services;

// ─── Interface ────────────────────────────────────────────────

public interface IReportService
{
    /// <summary>
    /// Student report card with subject results, attendance summary,
    /// and performance remarks. Returns a PDF byte array.
    /// </summary>
    Task<byte[]> GenerateStudentReportCardPdfAsync(
        Guid studentId,
        int academicYearId,
        string? examType = null,
        CancellationToken ct = default);

    /// <summary>
    /// A4 fee receipt with school branding, payment breakdown,
    /// and paid stamp. Returns a PDF byte array.
    /// </summary>
    Task<byte[]> GenerateFeeReceiptPdfAsync(
        long paymentId,
        CancellationToken ct = default);

    /// <summary>
    /// Monthly class attendance register as an Excel workbook.
    /// One row per student, one column per calendar day.
    /// Returns an .xlsx byte array.
    /// </summary>
    Task<byte[]> GenerateAttendanceSheetExcelAsync(
        int classId, int month, int year,
        CancellationToken ct = default);

    /// <summary>
    /// Exam result sheet with class statistics and grade distribution.
    /// Returns a PDF byte array.
    /// </summary>
    Task<byte[]> GenerateExamResultSheetPdfAsync(
        int examId,
        CancellationToken ct = default);
}

// ─── Implementation ───────────────────────────────────────────

public sealed class ReportService : IReportService
{
    private readonly IReportDataRepository _data;
    private readonly ILogoLoader _logo;
    private readonly ILogger<ReportService> _log;

    public ReportService(
        IReportDataRepository data,
        ILogoLoader logo,
        ILogger<ReportService> log)
    {
        _data = data;
        _logo = logo;
        _log = log;

        // Community licence — free for non-commercial / <$1M revenue
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. STUDENT REPORT CARD PDF
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<byte[]> GenerateStudentReportCardPdfAsync(
        Guid studentId, int academicYearId, string? examType = null,
        CancellationToken ct = default)
    {
        var d = await _data.GetReportCardDataAsync(studentId, academicYearId, ct);
        var colors = new BrandColors(d.School.PrimaryColor);
        var logo = await _logo.LoadAsync(d.School.LogoUrl, ct);

        _log.LogInformation(
            "Generating report card. Student={S} Year={Y}",
            studentId, academicYearId);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t =>
                    t.FontFamily("Arial")
                     .FontSize(9)
                     .FontColor(colors.Dark));

                // ── Header ────────────────────────────────────────────────
                page.Header().Element(h =>
                    SchoolHeaderComponent.Compose(
                        h, d.School, "Student Progress Report", colors, logo));

                // ── Content ───────────────────────────────────────────────
                page.Content().PaddingTop(10).Column(col =>
                {
                    // Student info card
                    StudentInfoCard(col, d.Header, colors);

                    col.Item().Height(8);

                    // Subject results table
                    SubjectResultsTable(col, d, colors);

                    col.Item().Height(8);

                    // Attendance + summary row
                    AttendanceSummaryRow(col, d.Attendance, colors);

                    col.Item().Height(10);

                    // Remarks + performance band
                    PerformanceRemarks(col, d, colors);

                    col.Item().Height(16);

                    // Signature block
                    SignatureBlock(col, colors);
                });

                // ── Footer ────────────────────────────────────────────────
                page.Footer().Element(f =>
                    PageFooterComponent.Compose(f, colors, d.School.Name));
            });
        })
        .GeneratePdf();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. FEE RECEIPT PDF
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<byte[]> GenerateFeeReceiptPdfAsync(
        long paymentId, CancellationToken ct = default)
    {
        var d = await _data.GetFeeReceiptDataAsync(paymentId, ct);
        var colors = new BrandColors(d.School.PrimaryColor);
        var logo = await _logo.LoadAsync(d.School.LogoUrl, ct);

        _log.LogInformation(
            "Generating receipt. PaymentId={P} Receipt={R}",
            paymentId, d.ReceiptNumber);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                // A5 — compact receipt format
                page.Size(PageSizes.A5);
                page.Margin(24);
                page.DefaultTextStyle(t =>
                    t.FontFamily("Arial")
                     .FontSize(9)
                     .FontColor(colors.Dark));

                page.Header().Element(h =>
                    SchoolHeaderComponent.Compose(
                        h, d.School, "Fee Payment Receipt", colors, logo));

                page.Content().PaddingTop(10).Column(col =>
                {
                    // Receipt metadata row
                    ReceiptMetaRow(col, d, colors);
                    col.Item().Height(8);

                    // Student info
                    ReceiptStudentBox(col, d, colors);
                    col.Item().Height(8);

                    // Fee breakdown table
                    FeeBreakdownTable(col, d, colors);
                    col.Item().Height(10);

                    // PAID watermark / status
                    ReceiptStatusBadge(col, d, colors);
                    col.Item().Height(10);

                    // Collected by + signature
                    ReceiptSignature(col, d, colors);
                });

                page.Footer().Element(f =>
                {
                    f.AlignCenter()
                     .Text("This is a computer-generated receipt.")
                     .FontSize(7)
                     .FontColor(colors.Gray)
                     .Italic();
                });
            });
        })
        .GeneratePdf();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. ATTENDANCE SHEET EXCEL
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<byte[]> GenerateAttendanceSheetExcelAsync(
        int classId, int month, int year, CancellationToken ct = default)
    {
        var d = await _data.GetAttendanceSheetDataAsync(classId, month, year, ct);
        var colors = new BrandColors(d.School.PrimaryColor);

        _log.LogInformation(
            "Generating attendance Excel. ClassId={C} Month={M}/{Y}",
            classId, month, year);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet($"{d.Header.ClassName}-{d.Header.Section} {d.Header.MonthName}");

        // Parse primary colour for ClosedXML
        var primaryXl = ParseXlColor(colors.Primary);
        var lightGrayXl = ParseXlColor(colors.LightGray);
        var borderXl = ParseXlColor(colors.Border);
        var darkXl = ParseXlColor(colors.Dark);
        var successXl = ParseXlColor(colors.Success);
        var dangerXl = ParseXlColor(colors.Danger);

        int daysInMonth = DateTime.DaysInMonth(year, month);

        // ── Row 1–3: School Header ─────────────────────────────────────────
        ws.Row(1).Height = 24;
        var titleCell = ws.Range(1, 1, 1, daysInMonth + 6).Merge();
        titleCell.Value = d.School.Name;
        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titleCell.Style.Font.FontSize = 14;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontColor = XLColor.FromHtml(colors.Primary);
        titleCell.Style.Fill.BackgroundColor = XLColor.White;

        ws.Row(2).Height = 14;
        if (!string.IsNullOrEmpty(d.School.Address))
        {
            var addrCell = ws.Range(2, 1, 2, daysInMonth + 6).Merge();
            addrCell.Value = d.School.Address;
            addrCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            addrCell.Style.Font.FontSize = 8;
            addrCell.Style.Font.FontColor = XLColor.FromHtml(colors.Gray);
        }

        // ── Row 4: Report title ────────────────────────────────────────────
        ws.Row(4).Height = 18;
        var reportTitleCell = ws.Range(4, 1, 4, daysInMonth + 6).Merge();
        reportTitleCell.Value = $"ATTENDANCE REGISTER — {d.Header.MonthName.ToUpperInvariant()}";
        reportTitleCell.Style.Font.Bold = true;
        reportTitleCell.Style.Font.FontSize = 11;
        reportTitleCell.Style.Font.FontColor = XLColor.White;
        reportTitleCell.Style.Fill.BackgroundColor = primaryXl;
        reportTitleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        reportTitleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // ── Row 5: Class info ──────────────────────────────────────────────
        ws.Row(5).Height = 14;
        ws.Cell(5, 1).Value = $"Class: {d.Header.ClassName} - {d.Header.Section}";
        ws.Cell(5, 1).Style.Font.Bold = true;
        ws.Cell(5, 1).Style.Font.FontSize = 8;

        int teacherCol = (daysInMonth / 2) + 2;
        ws.Cell(5, teacherCol).Value = $"Class Teacher: {d.Header.ClassTeacher}";
        ws.Cell(5, teacherCol).Style.Font.FontSize = 8;

        int wdCol = daysInMonth + 3;
        ws.Cell(5, wdCol).Value = $"Working Days: {d.Header.WorkingDays}";
        ws.Cell(5, wdCol).Style.Font.FontSize = 8;

        // ── Row 7: Column headers ──────────────────────────────────────────
        int headerRow = 7;
        ws.Row(headerRow).Height = 16;

        // Fixed columns
        StyleHeaderCell(ws.Cell(headerRow, 1), "#", primaryXl);
        StyleHeaderCell(ws.Cell(headerRow, 2), "Roll No", primaryXl);
        ws.Column(2).Width = 10;
        StyleHeaderCell(ws.Cell(headerRow, 3), "Student Name", primaryXl);
        ws.Column(3).Width = 22;

        // Day columns (4 .. daysInMonth+3)
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(year, month, day);
            var colIdx = day + 3;
            var cell = ws.Cell(headerRow, colIdx);
            cell.Value = day;
            cell.Style.Fill.BackgroundColor = primaryXl;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 7;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Column(colIdx).Width = 3.2;

            // Grey out Sunday columns
            if (date.DayOfWeek == DayOfWeek.Sunday)
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(colors.Gray);
        }

        // Summary columns
        int pCol = daysInMonth + 4;
        int aCol = daysInMonth + 5;
        int pctCol = daysInMonth + 6;
        StyleHeaderCell(ws.Cell(headerRow, pCol), "P", successXl);
        StyleHeaderCell(ws.Cell(headerRow, aCol), "A", dangerXl);
        StyleHeaderCell(ws.Cell(headerRow, pctCol), "Att%", primaryXl);
        ws.Column(pCol).Width = 4;
        ws.Column(aCol).Width = 4;
        ws.Column(pctCol).Width = 6;

        // ── Data rows ─────────────────────────────────────────────────────
        int dataStartRow = headerRow + 1;
        bool shaded = false;

        for (var i = 0; i < d.Rows.Count; i++)
        {
            var row = d.Rows[i];
            int rowNum = dataStartRow + i;
            shaded = i % 2 == 1;

            var xlBg = shaded ? lightGrayXl : XLColor.White;

            // Serial, roll, name
            SetDataCell(ws.Cell(rowNum, 1), (i + 1).ToString(), xlBg, darkXl, center: true);
            SetDataCell(ws.Cell(rowNum, 2), row.RollNumber, xlBg, darkXl, center: true);
            SetDataCell(ws.Cell(rowNum, 3), row.FullName, xlBg, darkXl);

            // Daily attendance columns
            for (var day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                var colIdx = day + 3;
                var status = row.DailyStatus.TryGetValue(day, out var s) ? s : "";
                var cell = ws.Cell(rowNum, colIdx);
                cell.Value = status;

                // Colour-code cells
                var cellBg = status switch
                {
                    "P" => XLColor.FromHtml("#DCFCE7"),   // green tint
                    "A" => XLColor.FromHtml("#FEE2E2"),   // red tint
                    "L" => XLColor.FromHtml("#FEF9C3"),   // yellow tint
                    "H" => XLColor.FromHtml("#F3F4F6"),   // grey
                    _ => date.DayOfWeek == DayOfWeek.Sunday
                           ? XLColor.FromHtml("#E5E7EB")
                           : xlBg
                };

                cell.Style.Fill.BackgroundColor = cellBg;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Font.FontSize = 7.5;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.BottomBorderColor = borderXl;
            }

            // Summary
            var pCell = ws.Cell(rowNum, pCol);
            pCell.Value = row.PresentCount;
            pCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCFCE7");
            pCell.Style.Font.Bold = true;
            pCell.Style.Font.FontSize = 8;
            pCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var aCell = ws.Cell(rowNum, aCol);
            aCell.Value = row.AbsentCount;
            aCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEE2E2");
            aCell.Style.Font.Bold = row.AbsentCount > 3;
            aCell.Style.Font.FontSize = 8;
            aCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var pctCell = ws.Cell(rowNum, pctCol);
            pctCell.Value = $"{row.AttendancePct:F1}%";
            pctCell.Style.Font.Bold = true;
            pctCell.Style.Font.FontSize = 8;
            pctCell.Style.Font.FontColor = row.AttendancePct < 75
                ? dangerXl : successXl;
            pctCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Row(rowNum).Height = 13;
        }

        // ── Totals row ────────────────────────────────────────────────────
        int totalsRow = dataStartRow + d.Rows.Count + 1;
        ws.Row(totalsRow).Height = 14;

        var totalsLabel = ws.Range(totalsRow, 1, totalsRow, 3).Merge();
        totalsLabel.Value = "CLASS TOTALS";
        totalsLabel.Style.Font.Bold = true;
        totalsLabel.Style.Font.FontSize = 8;
        totalsLabel.Style.Fill.BackgroundColor = primaryXl;
        totalsLabel.Style.Font.FontColor = XLColor.White;

        // Column totals per day
        for (var day = 1; day <= daysInMonth; day++)
        {
            var colIdx = day + 3;
            int pCount = d.Rows.Count(r =>
                r.DailyStatus.TryGetValue(day, out var s) && s == "P");
            var cell = ws.Cell(totalsRow, colIdx);
            cell.Value = pCount > 0 ? pCount.ToString() : "";
            cell.Style.Fill.BackgroundColor = primaryXl;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Font.FontSize = 7;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Total present / absent summary
        ws.Cell(totalsRow, pCol).Value = d.Rows.Sum(r => r.PresentCount);
        ws.Cell(totalsRow, aCol).Value = d.Rows.Sum(r => r.AbsentCount);
        foreach (var c in new[] { ws.Cell(totalsRow, pCol),
                                   ws.Cell(totalsRow, aCol),
                                   ws.Cell(totalsRow, pctCol) })
        {
            c.Style.Fill.BackgroundColor = primaryXl;
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Font.Bold = true;
            c.Style.Font.FontSize = 8;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // ── Legend ────────────────────────────────────────────────────────
        int legendRow = totalsRow + 2;
        ws.Cell(legendRow, 1).Value = "Legend:";
        ws.Cell(legendRow, 1).Style.Font.Bold = true;
        ws.Cell(legendRow, 1).Style.Font.FontSize = 8;

        var legend = new[] { ("P", "Present", "#DCFCE7"), ("A", "Absent", "#FEE2E2"),
                             ("L", "Leave", "#FEF9C3"),   ("H", "Holiday", "#F3F4F6") };
        for (var l = 0; l < legend.Length; l++)
        {
            int c = l * 2 + 2;
            var lc = ws.Cell(legendRow, c);
            lc.Value = legend[l].Item1;
            lc.Style.Fill.BackgroundColor = XLColor.FromHtml(legend[l].Item3);
            lc.Style.Font.FontSize = 8;
            lc.Style.Font.Bold = true;
            lc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var lc2 = ws.Cell(legendRow, c + 1);
            lc2.Value = legend[l].Item2;
            lc2.Style.Font.FontSize = 8;
        }

        // ── Freeze header rows ────────────────────────────────────────────
        ws.SheetView.FreezeRows(headerRow);
        ws.SheetView.FreezeColumns(3);

        // ── Print setup ───────────────────────────────────────────────────
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A3Paper;
        // ws.PageSetup.FitToPages = true;
        // ws.PageSetup.FitToWidth = 1;
        // ws.PageSetup.FitToHeight = 0;
        ws.PageSetup.Header.Center.AddText(
            $"{d.School.Name} — Attendance Register — {d.Header.MonthName}");
        // ws.PageSetup.Footer.Center.AddText("Page ")
        //     .AddPageNumber().AddText(" of ").AddNumberOfPages();

        // ── Column A width (serial) ────────────────────────────────────────
        ws.Column(1).Width = 4;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. EXAM RESULT SHEET PDF
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<byte[]> GenerateExamResultSheetPdfAsync(
        int examId, CancellationToken ct = default)
    {
        var d = await _data.GetExamResultSheetDataAsync(examId, ct);
        var colors = new BrandColors(d.School.PrimaryColor);
        var logo = await _logo.LoadAsync(d.School.LogoUrl, ct);

        _log.LogInformation(
            "Generating exam result sheet. ExamId={E}", examId);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t =>
                    t.FontFamily("Arial")
                     .FontSize(9)
                     .FontColor(colors.Dark));

                page.Header().Element(h =>
                    SchoolHeaderComponent.Compose(
                        h, d.School, "Examination Result Sheet", colors, logo));

                page.Content().PaddingTop(10).Column(col =>
                {
                    // Exam info metadata
                    ExamMetaCard(col, d.Header, colors);
                    col.Item().Height(8);

                    // Results table
                    ExamResultsTable(col, d, colors);
                    col.Item().Height(10);

                    // Statistics panel
                    ExamStatsPanel(col, d.Stats, colors);
                    col.Item().Height(16);

                    // Signature block
                    SignatureBlock(col, colors,
                        ("Examiner", "Subject Teacher", "Principal"));
                });

                page.Footer().Element(f =>
                    PageFooterComponent.Compose(f, colors, d.School.Name));
            });
        })
        .GeneratePdf();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE: Report Card sub-components
    // ═══════════════════════════════════════════════════════════════════════

    private static void StudentInfoCard(
        ColumnDescriptor col, ReportCardHeader h, BrandColors c)
    {
        col.Item()
           .Border(0.5f).BorderColor(c.Border)
           .Background(c.LightGray)
           .Padding(8)
           .Row(row =>
           {
               // Left: student details
               row.RelativeItem().Column(info =>
               {
                   InfoRow(info, "Student", h.StudentName, bold: true);
                   InfoRow(info, "Roll No", h.RollNumber);
                   InfoRow(info, "Class", $"{h.ClassName} - {h.Section}");
                   InfoRow(info, "Academic Yr", h.AcademicYear);
                   if (!string.IsNullOrEmpty(h.DateOfBirth))
                       InfoRow(info, "Date of Birth", h.DateOfBirth);
                   InfoRow(info, "Gender", h.Gender);
               });

               // Right: parent info
               row.RelativeItem().Column(info =>
               {
                   if (!string.IsNullOrEmpty(h.ParentName))
                       InfoRow(info, "Parent/Guardian", h.ParentName, bold: true);
                   if (!string.IsNullOrEmpty(h.ParentPhone))
                       InfoRow(info, "Contact", h.ParentPhone);
               });
           });
    }

    private static void InfoRow(ColumnDescriptor col, string label,
        string value, bool bold = false)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(85)
             .Text(label + ":")
             .FontSize(8).SemiBold();
            var t = r.RelativeItem()
                    .Text(value)
                    .FontSize(8);
            if (bold) t.Bold();
        });
    }

    private static void SubjectResultsTable(
        ColumnDescriptor col, ReportCardData d, BrandColors c)
    {
        col.Item()
           .Text("Academic Performance")
           .FontSize(9.5f).Bold().FontColor(c.Primary);

        col.Item().Height(4);

        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2.5f);  // Subject
                cols.RelativeColumn(2f);    // Exam
                cols.RelativeColumn(1f);    // Total
                cols.RelativeColumn(1f);    // Pass
                cols.RelativeColumn(1f);    // Obtained
                cols.RelativeColumn(0.8f);  // Grade
                cols.RelativeColumn(1f);    // %
                cols.RelativeColumn(1.2f);  // Remarks
            });

            // Header
            table.Header(h =>
            {
                TableStyles.HeaderCell(h.Cell(), "Subject", c);
                TableStyles.HeaderCell(h.Cell(), "Exam", c);
                TableStyles.HeaderCell(h.Cell(), "Total", c, alignRight: true);
                TableStyles.HeaderCell(h.Cell(), "Pass", c, alignRight: true);
                TableStyles.HeaderCell(h.Cell(), "Obtained", c, alignRight: true);
                TableStyles.HeaderCell(h.Cell(), "Grade", c);
                TableStyles.HeaderCell(h.Cell(), "%", c, alignRight: true);
                TableStyles.HeaderCell(h.Cell(), "Remark", c);
            });

            // Data rows
            bool shade = false;
            foreach (var row in d.Subjects)
            {
                var passed = !row.IsAbsent && row.MarksObtained >= row.PassMarks;
                var gradeClr = row.IsAbsent ? c.Warning
                             : passed ? c.Dark : c.Danger;

                TableStyles.DataCell(table.Cell(), row.SubjectName, c, shade);
                TableStyles.DataCell(table.Cell(), row.ExamName, c, shade);
                TableStyles.DataCell(table.Cell(), row.TotalMarks.ToString("0"), c, shade, alignRight: true);
                TableStyles.DataCell(table.Cell(), row.PassMarks.ToString("0"), c, shade, alignRight: true);
                TableStyles.DataCell(table.Cell(), row.IsAbsent ? "ABS"
                    : row.MarksObtained.ToString("0.0"), c, shade, alignRight: true,
                    bold: !passed, textColor: passed ? null : c.Danger);
                TableStyles.DataCell(table.Cell(), row.Grade ?? "-", c, shade,
                    textColor: gradeClr, bold: true);
                TableStyles.DataCell(table.Cell(), row.IsAbsent ? "-"
                    : $"{row.Percentage:F1}%", c, shade, alignRight: true);
                TableStyles.DataCell(table.Cell(),
                    row.IsAbsent ? "Absent"
                    : passed ? "Pass" : "Fail",
                    c, shade, textColor: row.IsAbsent ? c.Warning
                              : passed ? c.Success : c.Danger);

                shade = !shade;
            }

            // Totals row
            if (d.Subjects.Count > 0)
            {
                var totalSubjects = d.Subjects.Count(r => !r.IsAbsent);
                var totalObtained = d.Subjects.Where(r => !r.IsAbsent)
                                              .Sum(r => r.MarksObtained);
                var totalMax = d.Subjects.Where(r => !r.IsAbsent)
                                              .Sum(r => r.TotalMarks);
                var overallPct = totalMax == 0 ? 0m
                    : Math.Round(totalObtained / totalMax * 100, 1);

                void TotalCell(string v, bool right = false, bool bold2 = false)
                {
                    var t = table.Cell()
                                 .Background(c.LightGray)
                                 .PaddingVertical(5)
                                 .PaddingHorizontal(6);
                    var text = (right ? t.AlignRight() : t)
                               .Text(v).FontSize(8).FontColor(c.Dark);
                    if (bold2) text.Bold();
                }

                TotalCell("TOTAL", bold2: true);
                TotalCell(string.Empty);
                TotalCell(totalMax.ToString("0"), right: true);
                TotalCell(string.Empty);
                TotalCell(totalObtained.ToString("0.0"), right: true, bold2: true);
                TotalCell(string.Empty);
                TotalCell($"{overallPct:F1}%", right: true, bold2: true);
                TotalCell(string.Empty);
            }
        });
    }

    private static void AttendanceSummaryRow(
        ColumnDescriptor col, ReportCardAttendance a, BrandColors c)
    {
        col.Item().Row(row =>
        {
            // Attendance box
            row.RelativeItem()
               .Border(0.5f).BorderColor(c.Border)
               .Background(c.LightGray)
               .Padding(8)
               .Column(inner =>
               {
                   inner.Item()
                        .Text("Attendance Summary")
                        .FontSize(9).Bold().FontColor(c.Primary);
                   inner.Item().Height(4);

                   inner.Item().Table(t =>
                   {
                       t.ColumnsDefinition(cols =>
                       {
                           cols.RelativeColumn();
                           cols.RelativeColumn();
                           cols.RelativeColumn();
                           cols.RelativeColumn();
                           cols.RelativeColumn();
                       });

                       void AttHeader(string v) =>
                           TableStyles.HeaderCell(t.Cell(), v, c);
                       void AttData(string v, string? clr = null) =>
                           TableStyles.DataCell(t.Cell(), v, c, textColor: clr);

                       AttHeader("Total Days");
                       AttHeader("Present");
                       AttHeader("Absent");
                       AttHeader("Leave");
                       AttHeader("Attendance %");

                       AttData(a.TotalDays.ToString());
                       AttData(a.PresentDays.ToString(), c.Success);
                       AttData(a.AbsentDays.ToString(), a.AbsentDays > 5 ? c.Danger : null);
                       AttData(a.LeaveDays.ToString());
                       AttData($"{a.AttendancePct:F1}%",
                           a.AttendancePct >= 75 ? c.Success : c.Danger);
                   });
               });
        });
    }

    private static void PerformanceRemarks(
        ColumnDescriptor col, ReportCardData d, BrandColors c)
    {
        // Calculate overall percentage
        var subjects = d.Subjects.Where(r => !r.IsAbsent).ToList();
        var totalObt = subjects.Sum(r => r.MarksObtained);
        var totalMax = subjects.Sum(r => r.TotalMarks);
        var overallPct = totalMax == 0 ? 0m : totalObt / totalMax * 100m;

        var (band, bandColor, remark) = overallPct switch
        {
            >= 90 => ("Outstanding", c.Success, "Exceptional performance. Keep up the excellent work."),
            >= 80 => ("Excellent", c.Success, "Very good performance. Aim for even higher."),
            >= 70 => ("Very Good", c.Primary, "Good performance. Consistent effort needed."),
            >= 60 => ("Good", c.Primary, "Satisfactory performance. Focus on weaker subjects."),
            >= 50 => ("Average", c.Warning, "Needs improvement. Dedicated study required."),
            _ => ("Below Average", c.Danger, "Performance needs significant improvement.")
        };

        col.Item()
           .Border(0.5f).BorderColor(c.Border)
           .Padding(8)
           .Row(row =>
           {
               row.RelativeItem().Column(inner =>
               {
                   inner.Item().Text("Teacher's Remarks").FontSize(9).Bold().FontColor(c.Primary);
                   inner.Item().Height(4);
                   inner.Item().Text(remark).FontSize(8).FontColor(c.Dark);
               });

               row.ConstantItem(80)
                  .AlignCenter()
                  .Border(0.5f).BorderColor(bandColor)
                  .Background(c.LightGray)
                  .Padding(6)
                  .Column(badge =>
                  {
                      badge.Item().AlignCenter()
                           .Text($"{overallPct:F1}%")
                           .FontSize(14).Bold().FontColor(bandColor);
                      badge.Item().AlignCenter()
                           .Text(band)
                           .FontSize(7.5f).SemiBold().FontColor(bandColor);
                  });
           });
    }

    private static void SignatureBlock(
        ColumnDescriptor col, BrandColors c,
        (string, string, string)? labels = null)
    {
        var (l1, l2, l3) = labels ?? ("Class Teacher", "Subject Teacher", "Principal");

        col.Item().Row(row =>
        {
            foreach (var label in new[] { l1, l2, l3 })
            {
                row.RelativeItem().Column(sig =>
                {
                    sig.Item().Height(28);  // Space for signature
                    sig.Item()
                       .BorderTop(0.5f).BorderColor(c.Border)
                       .PaddingTop(4)
                       .AlignCenter()
                       .Text(label)
                       .FontSize(8).FontColor(c.Gray);
                });

                if (label != l3)
                    row.ConstantItem(20); // Gap between sig blocks
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE: Fee Receipt sub-components
    // ═══════════════════════════════════════════════════════════════════════

    private static void ReceiptMetaRow(
        ColumnDescriptor col, FeeReceiptData d, BrandColors c)
    {
        col.Item()
           .Background(c.LightGray)
           .Padding(6)
           .Row(row =>
           {
               row.RelativeItem().Column(left =>
               {
                   left.Item().Text(t =>
                   {
                       t.Span("Receipt No: ").FontSize(8).SemiBold();
                       t.Span(d.ReceiptNumber)
                           .FontSize(9).Bold().FontColor(c.Primary);
                   });
               });

               row.AutoItem().AlignRight().Column(right =>
               {
                   right.Item().Text(t =>
                   {
                       t.Span("Date: ").FontSize(8).SemiBold();
                       t.Span(d.PaymentDate.ToString("dd MMM yyyy")).FontSize(8);
                   });
                   right.Item().Text(t =>
                   {
                       t.Span("Method: ").FontSize(8).SemiBold();
                       t.Span(d.PaymentMethod).FontSize(8);
                   });
                   if (!string.IsNullOrEmpty(d.ReferenceNumber))
                       right.Item().Text(t =>
                       {
                           t.Span("Ref: ").FontSize(8).SemiBold();
                           t.Span(d.ReferenceNumber).FontSize(8);
                       });
               });
           });
    }

    private static void ReceiptStudentBox(
        ColumnDescriptor col, FeeReceiptData d, BrandColors c)
    {
        col.Item()
           .Border(0.5f).BorderColor(c.Border)
           .Padding(7)
           .Column(inner =>
           {
               inner.Item().Text("Student Details")
                    .FontSize(8.5f).Bold().FontColor(c.Primary);
               inner.Item().Height(4);

               void Row2(string lbl, string val) =>
                   inner.Item().Row(r =>
                   {
                       r.ConstantItem(80).Text(lbl + ":").FontSize(8).SemiBold();
                       r.RelativeItem().Text(val).FontSize(8);
                   });

               Row2("Name", d.StudentName);
               Row2("Roll No", d.RollNumber);
               Row2("Class", d.ClassName);
           });
    }

    private void FeeBreakdownTable(
        ColumnDescriptor col, FeeReceiptData d, BrandColors c)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3);
                cols.RelativeColumn(1.5f);
            });

            // Header
            table.Header(h =>
            {
                TableStyles.HeaderCell(h.Cell(), "Description", c);
                TableStyles.HeaderCell(h.Cell(), "Amount", c, alignRight: true);
            });

            void FeeRow(string desc, string amt, bool shade2 = false, string? clr = null)
            {
                TableStyles.DataCell(table.Cell(), desc, c, shade2);
                TableStyles.DataCell(table.Cell(), amt, c, shade2,
                    alignRight: true, textColor: clr);
            }

            FeeRow(d.FeeTypeName,
                   $"{d.School.Currency} {d.AmountDue:N2}");

            if (d.Discount > 0)
                FeeRow("Discount",
                       $"- {d.School.Currency} {d.Discount:N2}", shade2: true,
                       clr: c.Success);

            if (d.Fine > 0)
                FeeRow("Late Fine",
                       $"+ {d.School.Currency} {d.Fine:N2}", shade2: false,
                       clr: c.Danger);

            // Amount Paid row (prominent)
            table.Cell()
                 .Background(c.LightGray)
                 .PaddingVertical(6).PaddingHorizontal(6)
                 .Text("Amount Paid")
                 .FontSize(8.5f).Bold();
            table.Cell()
                 .Background(c.LightGray)
                 .PaddingVertical(6).PaddingHorizontal(6)
                 .AlignRight()
                 .Text($"{d.School.Currency} {d.AmountPaid:N2}")
                 .FontSize(9).Bold().FontColor(c.Success);

            // Balance
            table.Cell()
                 .Border(0.5f).BorderColor(c.Border)
                 .PaddingVertical(5).PaddingHorizontal(6)
                 .Text("Balance Due").FontSize(8);
            table.Cell()
                 .Border(0.5f).BorderColor(c.Border)
                 .PaddingVertical(5).PaddingHorizontal(6)
                 .AlignRight()
                 .Text($"{d.School.Currency} {d.BalanceDue:N2}")
                 .FontSize(8.5f).Bold()
                 .FontColor(d.BalanceDue > 0 ? c.Danger : c.Success);
        });
    }

    private static void ReceiptStatusBadge(
        ColumnDescriptor col, FeeReceiptData d, BrandColors c)
    {
        var isPaid = d.BalanceDue <= 0;
        var badgeColor = isPaid ? c.Success : c.Warning;
        var badgeText = isPaid ? "PAID IN FULL" : "PARTIAL PAYMENT";

        col.Item()
           .AlignCenter()
           .Border(2).BorderColor(badgeColor)
           .Padding(6)
           .Text(badgeText)
           .FontSize(13).Bold().FontColor(badgeColor)
           .LetterSpacing(2f);
    }

    private static void ReceiptSignature(
        ColumnDescriptor col, FeeReceiptData d, BrandColors c)
    {
        col.Item().Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(t =>
                {
                    t.Span("Collected by: ").FontSize(8).SemiBold();
                    t.Span(d.CollectedBy).FontSize(8);
                });
            });
            row.AutoItem()
               .AlignRight()
               .Column(right =>
               {
                   right.Item().Height(20);
                   right.Item()
                        .BorderTop(0.5f).BorderColor(c.Border)
                        .PaddingTop(3)
                        .AlignRight()
                        .Text("Authorised Signatory")
                        .FontSize(7.5f).FontColor(c.Gray);
               });
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE: Exam Result sub-components
    // ═══════════════════════════════════════════════════════════════════════

    private static void ExamMetaCard(
        ColumnDescriptor col, ExamResultHeader h, BrandColors c)
    {
        col.Item()
           .Background(c.LightGray)
           .Border(0.5f).BorderColor(c.Border)
           .Padding(8)
           .Row(row =>
           {
               void MetaItem(string label, string value)
               {
                   row.RelativeItem().Column(inner =>
                   {
                       inner.Item().Text(label).FontSize(7).FontColor(c.Gray);
                       inner.Item().Text(value).FontSize(9).Bold().FontColor(c.Dark);
                   });
               }

               MetaItem("Exam", h.ExamName);
               MetaItem("Subject", h.SubjectName);
               MetaItem("Class", $"{h.ClassName} - {h.Section}");
               MetaItem("Date", h.ExamDate.ToString("dd MMM yyyy"));
               MetaItem("Total Marks", h.TotalMarks.ToString("0"));
               MetaItem("Pass Marks", h.PassMarks.ToString("0"));
               MetaItem("Academic Yr", h.AcademicYear);
           });
    }

    private static void ExamResultsTable(
        ColumnDescriptor col, ExamResultSheetData d, BrandColors c)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(22);    // #
                cols.RelativeColumn(1f);    // Roll
                cols.RelativeColumn(3f);    // Name
                cols.RelativeColumn(1.2f);  // Marks
                cols.RelativeColumn(0.8f);  // Grade
                cols.RelativeColumn(1f);    // %
                cols.RelativeColumn(0.8f);  // Result
            });

            table.Header(h =>
            {
                TableStyles.HeaderCell(h.Cell(), "#", c);
                TableStyles.HeaderCell(h.Cell(), "Roll No", c);
                TableStyles.HeaderCell(h.Cell(), "Student Name", c);
                TableStyles.HeaderCell(h.Cell(), "Marks", c, alignRight: true);
                TableStyles.HeaderCell(h.Cell(), "Grade", c);
                TableStyles.HeaderCell(h.Cell(), "%", c, alignRight: true);
                TableStyles.HeaderCell(h.Cell(), "Result", c);
            });

            bool shade = false;
            int serial = 1;

            foreach (var row in d.Rows)
            {
                var resultText = row.IsAbsent ? "ABSENT"
                                : row.Passed ? "PASS" : "FAIL";
                var resultColor = row.IsAbsent ? c.Warning
                                : row.Passed ? c.Success : c.Danger;

                TableStyles.DataCell(table.Cell(), serial++.ToString(), c, shade);
                TableStyles.DataCell(table.Cell(), row.RollNumber, c, shade);
                TableStyles.DataCell(table.Cell(), row.StudentName, c, shade);
                TableStyles.DataCell(table.Cell(),
                    row.IsAbsent ? "ABS" : row.MarksObtained.ToString("0.0"),
                    c, shade, alignRight: true,
                    textColor: row.Passed ? null : c.Danger);
                TableStyles.DataCell(table.Cell(), row.Grade ?? "-", c, shade,
                    bold: true, textColor: resultColor);
                TableStyles.DataCell(table.Cell(),
                    row.IsAbsent ? "-" : $"{row.Percentage:F1}%",
                    c, shade, alignRight: true);
                TableStyles.DataCell(table.Cell(), resultText, c, shade,
                    bold: true, textColor: resultColor);

                shade = !shade;
            }
        });
    }

    private static void ExamStatsPanel(
        ColumnDescriptor col, ExamResultStats s, BrandColors c)
    {
        col.Item()
           .Background(c.LightGray)
           .Border(0.5f).BorderColor(c.Border)
           .Padding(8)
           .Column(inner =>
           {
               inner.Item()
                    .Text("Class Statistics")
                    .FontSize(9.5f).Bold().FontColor(c.Primary);
               inner.Item().Height(6);

               inner.Item().Row(row =>
               {
                   void StatBox(string label, string value, string? clr = null)
                   {
                       row.RelativeItem()
                          .Border(0.5f).BorderColor(c.Border)
                          .Background(c.White)
                          .Padding(6)
                          .AlignCenter()
                          .Column(box =>
                          {
                              box.Item().AlignCenter()
                                 .Text(value)
                                 .FontSize(13).Bold()
                                 .FontColor(clr ?? c.Dark);
                              box.Item().AlignCenter()
                                 .Text(label)
                                 .FontSize(7).FontColor(c.Gray);
                          });
                       row.ConstantItem(4);
                   }

                   StatBox("Total", s.TotalStudents.ToString());
                   StatBox("Passed", s.Passed.ToString(), c.Success);
                   StatBox("Failed", s.Failed.ToString(), s.Failed > 0 ? c.Danger : null);
                   StatBox("Absent", s.Absent.ToString(), s.Absent > 0 ? c.Warning : null);
                   StatBox("Pass Rate", $"{s.PassPct:F1}%",
                       s.PassPct >= 70 ? c.Success : c.Warning);
                   StatBox("Average", s.AverageMarks.ToString("F1"), c.Primary);
                   StatBox("Highest", s.HighestMarks.ToString("0"), c.Success);
                   StatBox("Lowest", s.LowestMarks.ToString("0"),
                       s.LowestMarks < 40 ? c.Danger : null);
               });
           });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Excel helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static void StyleHeaderCell(IXLCell cell, string value, XLColor bg)
    {
        cell.Value = value;
        cell.Style.Fill.BackgroundColor = bg;
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 8;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void SetDataCell(IXLCell cell, string value,
        XLColor bg, XLColor fg, bool center = false)
    {
        cell.Value = value;
        cell.Style.Fill.BackgroundColor = bg;
        cell.Style.Font.FontColor = fg;
        cell.Style.Font.FontSize = 8;
        cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#E5E7EB");
        if (center) cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static XLColor ParseXlColor(string hex)
    {
        try { return XLColor.FromHtml(hex); }
        catch { return XLColor.Blue; }
    }
}