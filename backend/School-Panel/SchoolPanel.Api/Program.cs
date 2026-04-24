using SchoolPanel.Api.Configuration;
using SchoolPanel.Api.Extensions;
using SchoolPanel.Api.Middleware;
using SchoolPanel.Auth.Middleware;
using SchoolPanel.Auth.Extensions;
using Serilog;
using System.Globalization;

// ═══════════════════════════════════════════════════════════════════════════════
// SCHOOL MANAGEMENT ADMIN PANEL — ASP.NET Core 8 API
// Program.cs — Application Bootstrap
// Build order: Serilog → Options → Services → Middleware Pipeline
// ═══════════════════════════════════════════════════════════════════════════════

// ── Bootstrap Serilog immediately so startup errors are captured ───────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

Log.Information("Starting School Management Panel API...");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog — full config from appsettings ─────────────────────────────────
    builder.Host.UseSerilog((context, services, configuration) =>
        configuration.ReadFrom.Configuration(context.Configuration)
                     .ReadFrom.Services(services)
                     .Enrich.FromLogContext());

    // ── Configuration ─────────────────────────────────────────────────────────
    var config = builder.Configuration;

    // ── Strongly-typed options (IOptions<T> pattern) ──────────────────────────
    builder.Services.AddAppOptions(config);

    // ── Controllers (MVC Controllers chosen over Minimal APIs)
    // Justification: School panel has 40+ endpoints with complex auth, validation,
    // filters, and DI. Controllers give cleaner separation, built-in model
    // binding/validation, attribute routing, and easier Swagger documentation
    // at this scale. Minimal APIs shine for simple microservices — not here.
    builder.Services.AddControllers(options =>
    {
        options.ReturnHttpNotAcceptable = true;     // 406 on unsupported Accept headers
        options.SuppressAsyncSuffixInActionNames = false;
    });

    // ── Swagger / OpenAPI ─────────────────────────────────────────────────────
    builder.Services.AddSwaggerWithJwt();

    // ── Authentication (JWT Bearer + HS512) ────────────────────────────────────
    builder.Services.AddSchoolPanelAuth(config);

    // ── Custom filters (e.g. login lockout) ───────────────────────────────────
    builder.Services.AddSingleton<SchoolPanel.Auth.Filters.IpLoginAttemptTracker>();
    builder.Services.AddScoped<SchoolPanel.Auth.Filters.LoginLockoutFilter>();

    // ── Authorization (dynamic permission policies) ────────────────────────────
    // builder.Services.AddPermissionAuthorization();

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddAppCors(config);

    // ── Rate Limiting ─────────────────────────────────────────────────────────
    builder.Services.AddAppRateLimiting(config);

    // ── Repositories + Services ───────────────────────────────────────────────
    builder.Services.AddRepositories();
    builder.Services.AddAppServices();

    // ── HttpClient factory (for Google OAuth) ─────────────────────────────────
    builder.Services.AddHttpClient();

    // ── Health Checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddSqlServer(
            config.GetConnectionString("DefaultConnection")!,
            name: "sql-server",
            tags: ["db", "ready"]);

    // ── Problem Details (RFC 7807) ─────────────────────────────────────────────
    builder.Services.AddProblemDetails();

    // ── Response Compression ──────────────────────────────────────────────────
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
    });

    // ── Memory Cache (for Standard+ API response caching) ─────────────────────
    builder.Services.AddMemoryCache();

    // ── Response Caching ──────────────────────────────────────────────────────
    builder.Services.AddResponseCaching();

    // ═══════════════════════════════════════════════════════════════════════════
    // BUILD
    // ═══════════════════════════════════════════════════════════════════════════
    var app = builder.Build();

    // ═══════════════════════════════════════════════════════════════════════════
    // MIDDLEWARE PIPELINE
    // ORDER IS CRITICAL — each layer must come before what it wraps
    // ═══════════════════════════════════════════════════════════════════════════

    // ── 1. Exception handling — outermost, catches everything below ────────────
    app.UseMiddleware<ExceptionMiddleware>();

    // ── 2. HTTPS redirection + HSTS ───────────────────────────────────────────
    var security = config.GetSection(SchoolPanel.Api.Configuration.SecurityOptions.Section).Get<SchoolPanel.Api.Configuration.SecurityOptions>()
                ?? new SchoolPanel.Api.Configuration.SecurityOptions();

    if (security.RequireHttps)
    {
        app.UseHttpsRedirection();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts(); // Adds Strict-Transport-Security header
        }
    }

    // ── 3. Response compression ────────────────────────────────────────────────
    app.UseResponseCompression();

    // ── 4. Swagger — dev + staging only ──────────────────────────────────────
    if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "School Panel API v1");
            c.RoutePrefix = "swagger";
            c.DisplayRequestDuration();
            c.EnableTryItOutByDefault();
        });
    }

    // ── 5. Serilog request logging ─────────────────────────────────────────────
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent",
                httpContext.Request.Headers["User-Agent"].ToString());
        };
    });

    // ── 6. CORS — before routing and auth ─────────────────────────────────────
    app.UseCors(CorsOptions.PolicyName);

    // ── 7. Rate limiting ──────────────────────────────────────────────────────
    app.UseRateLimiter();

    // ── 8. Response caching ────────────────────────────────────────────────────
    app.UseResponseCaching();

    // ── 9. Routing ────────────────────────────────────────────────────────────
    app.UseRouting();

    // ── 10. Authentication (verify JWT) ───────────────────────────────────────
    app.UseAuthentication();

    // ── 11. Authorization (check policies/roles) ──────────────────────────────
    app.UseAuthorization();

    // ── 12. Audit logging — after auth so UserId is populated ─────────────────
    app.UseMiddleware<AuditLoggingMiddleware>();

    // ── 13. Security headers ──────────────────────────────────────────────────
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
        context.Response.Headers.Remove("Server");                  // Don't reveal server type
        context.Response.Headers.Remove("X-Powered-By");
        await next();
    });

    // ── 14. Health checks ─────────────────────────────────────────────────────
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });

    // ── 15. Controllers ───────────────────────────────────────────────────────
    app.MapControllers();

    // ── 16. Root redirect → Swagger ───────────────────────────────────────────
    app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous();

    // ═══════════════════════════════════════════════════════════════════════════
    // RUN
    // ═══════════════════════════════════════════════════════════════════════════
    Log.Information(
        "School Management Panel API started. Environment={Environment}",
        app.Environment.EnvironmentName);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application failed to start.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;