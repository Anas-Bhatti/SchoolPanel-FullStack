// ============================================================
// Middleware/AuditLoggingMiddleware.cs
// Logs BEFORE and AFTER every endpoint execution.
//
// Before: records the intent (what was attempted, who, from where)
// After:  records the outcome (success/failure, status code)
//
// Calls sp_InsertAuditLog via IAuthDataService (fire-and-forget
// on success paths; awaited on failure paths for reliability).
//
// Skipped for: GET requests, health checks, swagger, static files
// ============================================================

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolPanel.Auth.Services;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;

namespace SchoolPanel.Auth.Middleware;

public sealed class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    // Methods that mutate state — skip GET / HEAD / OPTIONS
    private static readonly FrozenSet<string> MutatingMethods =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "POST", "PUT", "PATCH", "DELETE" }
        .ToFrozenSet();

    // Path prefixes to skip entirely
    private static readonly string[] SkipPrefixes =
    [
        "/health",
        "/swagger",
        "/favicon",
        "/_blazor",
        "/api/auth/refresh",     // High-frequency rotation — not a state change audit
    ];

    // Endpoint action → readable audit action name
    private static readonly Dictionary<string, string> ActionMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "POST",   "Create" },
            { "PUT",    "Update" },
            { "PATCH",  "Update" },
            { "DELETE", "Delete" }
        };

    // Auth-specific endpoint actions
    private static readonly Dictionary<string, string> EndpointActionOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "/api/auth/login",          "Login"              },
            { "/api/auth/login/2fa-verify","LoginTwoFactor"    },
            { "/api/auth/logout",         "Logout"             },
            { "/api/auth/google-login",   "GoogleLogin"        },
            { "/api/auth/2fa/setup",      "TwoFactorSetup"     },
            { "/api/auth/2fa/verify-setup","TwoFactorActivated"},
            { "/api/auth/2fa/disable",    "TwoFactorDisabled"  },
        };

    public AuditLoggingMiddleware(
        RequestDelegate next,
        ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // ── Skip non-auditable paths ──────────────────────────────────────────
        if (!ShouldAudit(context))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        var requestId = context.TraceIdentifier;
        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;
        var ip = GetClientIp(context);
        var userAgent = context.Request.Headers["User-Agent"].ToString();

        // ── Capture identity BEFORE execution ────────────────────────────────
        // (Identity exists in token even before controller runs)
        var (userId, userEmail) = GetUserIdentity(context);
        var action = ResolveAction(path, method);
        var module = ResolveModule(path);

        _logger.LogDebug(
            "Audit[Before] ReqId={ReqId} {Method} {Path} User={Email}",
            requestId, method, path, userEmail ?? "anonymous");

        // ── Execute pipeline ──────────────────────────────────────────────────
        Exception? caughtEx = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            caughtEx = ex;
            throw; // Re-throw — ExceptionMiddleware handles the response
        }
        finally
        {
            sw.Stop();

            // Identity may now be populated (auth middleware ran)
            if (userId == null)
                (userId, userEmail) = GetUserIdentity(context);

            var statusCode = context.Response.StatusCode;
            var isSuccess = caughtEx == null && statusCode < 400;
            var errMessage = caughtEx?.Message
                           ?? (statusCode >= 400 ? $"HTTP {statusCode}" : null);

            var recordId = ExtractRecordId(context);
            var description = $"{method} {path} → {statusCode} ({sw.ElapsedMilliseconds}ms)";

            _logger.LogDebug(
                "Audit[After] ReqId={ReqId} Status={Status} Elapsed={Ms}ms Success={Ok}",
                requestId, statusCode, sw.ElapsedMilliseconds, isSuccess);

            // ── Fire-and-forget on success / await on failure ─────────────────
            // Failure audit is critical; success audit is best-effort.
            if (isSuccess)
            {
                _ = WriteAuditAsync(
                    context, userId, userEmail, action, module,
                    recordId, description, ip, userAgent, true, null);
            }
            else
            {
                await WriteAuditAsync(
                    context, userId, userEmail, action, module,
                    recordId, description, ip, userAgent, false, errMessage);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Write to DB via IAuthDataService (resolves from DI scope)
    // ─────────────────────────────────────────────────────────────────────────
    private static async Task WriteAuditAsync(
        HttpContext ctx,
        Guid? userId,
        string? userEmail,
        string action,
        string module,
        string? recordId,
        string description,
        string ip,
        string userAgent,
        bool isSuccess,
        string? errorMessage)
    {
        try
        {
            // Create a new scope so we don't extend the request scope lifetime
            await using var scope = ctx.RequestServices
                .CreateAsyncScope();

            var svc = scope.ServiceProvider
                .GetRequiredService<IAuthDataService>();

            await svc.InsertAuditLogAsync(
                userId,
                userEmail,
                action,
                module,
                recordId,
                description,
                ip,
                userAgent,
                isSuccess,
                errorMessage);
        }
        catch
        {
            // Audit write must never propagate — swallow silently
            // (already logged by InsertAuditLogAsync)
        }
    }

    // ─── Route resolution helpers ─────────────────────────────────────────────

    private static bool ShouldAudit(HttpContext context)
    {
        if (!MutatingMethods.Contains(context.Request.Method))
            return false;

        var path = context.Request.Path.Value ?? string.Empty;
        return !SkipPrefixes.Any(p =>
            path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveAction(string path, string method)
    {
        if (EndpointActionOverrides.TryGetValue(path, out var override_))
            return override_;

        return ActionMap.TryGetValue(method, out var action) ? action : method;
    }

    private static string ResolveModule(string path)
    {
        // /api/{module}/... → capitalise module name
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            var seg = segments[1];
            return char.ToUpperInvariant(seg[0]) + seg[1..];
        }
        return "Unknown";
    }

    private static string? ExtractRecordId(HttpContext context)
    {
        // Try route values first (most reliable)
        var route = context.GetRouteData();
        if (route?.Values.TryGetValue("id", out var routeId) == true)
            return routeId?.ToString();

        // Fallback: last path segment if it looks like a GUID or int
        var segments = (context.Request.Path.Value ?? "")
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length > 0)
        {
            var last = segments[^1];
            if (Guid.TryParse(last, out _) || int.TryParse(last, out _))
                return last;
        }

        return null;
    }

    private static (Guid? UserId, string? Email) GetUserIdentity(HttpContext context)
    {
        var userIdStr = context.User.FindFirstValue(TokenService.ClaimUserId)
                     ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue("sub");

        var email = context.User.FindFirstValue(ClaimTypes.Email)
                 ?? context.User.FindFirstValue("email");

        Guid? userId = Guid.TryParse(userIdStr, out var id) ? id : null;
        return (userId, email);
    }

    private static string GetClientIp(HttpContext ctx)
    {
        var fwd = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(fwd))
            return fwd.Split(',')[0].Trim();
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

// ─── Extension method for clean registration ──────────────────

public static class AuditMiddlewareExtensions
{
    /// <summary>
    /// Register AuditLoggingMiddleware.
    /// Must be placed AFTER UseAuthentication() + UseAuthorization()
    /// so the JWT identity is available before we read it.
    /// </summary>
    public static IApplicationBuilder UseAuditLogging(
        this IApplicationBuilder app)
        => app.UseMiddleware<AuditLoggingMiddleware>();
}