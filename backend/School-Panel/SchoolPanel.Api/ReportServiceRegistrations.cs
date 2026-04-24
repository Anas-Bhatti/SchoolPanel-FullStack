// ============================================================
// ReportServiceRegistrations.cs
// Single extension method to wire all report services in DI.
// Call from Program.cs: builder.Services.AddReportServices()
// ============================================================

using Microsoft.Extensions.DependencyInjection;
using SchoolPanel.Reports.Helpers;
using SchoolPanel.Reports.Services;

namespace SchoolPanel.Reports;

public static class ReportServiceRegistrations
{
    /// <summary>
    /// Register the complete report generation pipeline:
    ///   - IReportDataRepository (Scoped — one per request, holds DB connection)
    ///   - ILogoLoader           (Singleton — logo bytes cached in memory)
    ///   - IReportService        (Scoped — uses repository + logo loader)
    ///
    /// Also requires in Program.cs:
    ///   builder.Services.AddMemoryCache()
    ///   builder.Services.AddHttpClient()
    /// </summary>
    public static IServiceCollection AddReportServices(
        this IServiceCollection services)
    {
        // Scoped: IReportDataRepository holds Dapper connections
        services.AddScoped<IReportDataRepository, ReportDataRepository>();

        // Singleton: logo bytes are cached for 30 minutes — logos rarely change
        services.AddSingleton<ILogoLoader, LogoLoader>();

        // Scoped: main service composes data + rendering per request
        services.AddScoped<IReportService, ReportService>();

        return services;
    }
}

/*
─────────────────────────────────────────────────────────────────────────────
 PROGRAM.CS WIRING (add to your existing Program.cs)
─────────────────────────────────────────────────────────────────────────────

 using SchoolPanel.Reports;

 // Required prerequisites
 builder.Services.AddMemoryCache();
 builder.Services.AddHttpClient("LogoLoader", client =>
 {
     client.Timeout = TimeSpan.FromSeconds(10);
 });

 // Register report services
 builder.Services.AddReportServices();

─────────────────────────────────────────────────────────────────────────────
 PERMISSION POLICIES REQUIRED (already registered in AuthenticationExtensions)
─────────────────────────────────────────────────────────────────────────────

 "Reports.View"    — view report card, fee receipt, exam results
 "Reports.Export"  — download attendance Excel
 "Fees.View"       — fee receipt endpoint also uses this

─────────────────────────────────────────────────────────────────────────────
 ENDPOINT SUMMARY
─────────────────────────────────────────────────────────────────────────────

 GET /api/reports/report-card/{studentId:guid}
     ?academicYearId=1 &examType=Annual &download=false
     → application/pdf
     → Policy: Reports.View

 GET /api/reports/fee-receipt/{paymentId:long}
     ?download=false
     → application/pdf
     → Policy: Fees.View

 GET /api/reports/attendance/excel
     ?classId=3 &month=11 &year=2024
     → application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
     → Policy: Reports.Export

 GET /api/reports/exam-results/{examId:int}
     ?download=false
     → application/pdf
     → Policy: Reports.View

─────────────────────────────────────────────────────────────────────────────
 SETTINGS KEYS READ FROM dbo.Settings (ensure these exist)
─────────────────────────────────────────────────────────────────────────────

 School.Name          → e.g. "City Grammar School"
 School.Address       → e.g. "123 Main Street, Lahore"
 School.Phone         → e.g. "+92 42 1234567"
 School.Email         → e.g. "info@school.edu"
 School.Website       → e.g. "www.school.edu"
 School.LogoUrl       → Azure Blob URL or empty
 Theme.PrimaryColor   → e.g. "#2563EB"
 System.Currency      → e.g. "Rs."
 System.DateFormat    → e.g. "dd MMM yyyy"

─────────────────────────────────────────────────────────────────────────────
*/