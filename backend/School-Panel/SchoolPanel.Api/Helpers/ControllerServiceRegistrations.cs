// ============================================================
// Helpers/ControllerServiceRegistrations.cs
// Extension methods to register all controller dependencies
// in Program.cs with a single AddSchoolPanelControllers() call.
// ============================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolPanel.Controllers.Services;

namespace SchoolPanel.Controllers.Helpers;

public static class ControllerServiceRegistrations
{
    /// <summary>
    /// Register all services needed by the three feature controllers.
    /// Call from Program.cs: builder.Services.AddSchoolPanelControllers(config)
    /// </summary>
    public static IServiceCollection AddSchoolPanelControllers(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ── Options ───────────────────────────────────────────────────────────
        services.Configure<BlobOptions>(
            config.GetSection(BlobOptions.Section));

        services.Configure<Controllers.SecuritySettings>(
            config.GetSection(Controllers.SecuritySettings.Section));

        // ── Storage & file services ───────────────────────────────────────────
        services.AddScoped<IBlobStorageService, BlobStorageService>();

        // ── PDF generation (QuestPDF) ─────────────────────────────────────────
        services.AddSingleton<IPdfReceiptService, PdfReceiptService>();

        // ── Excel import (ClosedXML) ──────────────────────────────────────────
        services.AddSingleton<IExcelImportService, ExcelImportService>();

        // ── Response caching (for today-attendance + fee-summary) ─────────────
        services.AddResponseCaching();

        return services;
    }
}

// ============================================================
// Helpers/ProgramExtensions.cs
// Middleware registration for the feature controllers.
// ============================================================

// namespace block

public static class ProgramExtensions
{
    /// <summary>
    /// Add response caching middleware.
    /// Must be placed after UseRouting() and before UseAuthorization().
    /// </summary>
    public static WebApplication UseSchoolPanelControllers(
        this WebApplication app)
    {
        app.UseResponseCaching();
        return app;
    }
}

// ============================================================
// Program.cs snippet showing complete wiring
// (Copy into your actual Program.cs — do not compile as-is)
// ============================================================

/*

using SchoolPanel.Controllers.Helpers;

var builder = WebApplication.CreateBuilder(args);

// ── Auth backbone (from Phase 3) ──────────────────────────────
builder.Services.AddSchoolPanelAuth(builder.Configuration);

// ── Controller feature services ───────────────────────────────
builder.Services.AddSchoolPanelControllers(builder.Configuration);

// ── MVC Controllers ───────────────────────────────────────────
builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
    options.ReturnHttpNotAcceptable          = true;
});

// ── Rate limiting ─────────────────────────────────────────────
builder.Services.AddAppRateLimiting(builder.Configuration);

// ── Swagger ───────────────────────────────────────────────────
builder.Services.AddSwaggerWithJwt();

var app = builder.Build();

// ── Pipeline order ────────────────────────────────────────────
app.UseMiddleware<ExceptionMiddleware>();

if (builder.Configuration["Security:RequireHttps"] == "true")
    app.UseHttpsRedirection();

app.UseSwagger();
app.UseSwaggerUI();
app.UseSerilogRequestLogging();
app.UseCors("SchoolPanelCors");
app.UseRateLimiter();
app.UseResponseCaching();     // Must come before UseAuthorization
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditLoggingMiddleware>();  // After auth so user identity is populated

// Security headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Append("X-Content-Type-Options",  "nosniff");
    ctx.Response.Headers.Append("X-Frame-Options",         "DENY");
    ctx.Response.Headers.Append("X-XSS-Protection",        "1; mode=block");
    ctx.Response.Headers.Remove("Server");
    await next();
});

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

*/