// ============================================================
// Filters/LoginLockoutFilter.cs
// Action filter applied specifically to the /login endpoint.
// Runs BEFORE the controller action — rejects locked accounts
// and applies IP-level rate limiting for auth endpoints.
//
// Registered as: [ServiceFilter(typeof(LoginLockoutFilter))]
// on the Login action only.
// ============================================================

using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolPanel.Auth.DTOs;
using SchoolPanel.Auth.Extensions;

namespace SchoolPanel.Auth.Filters;

// ─── IP-level in-memory tracker (rate limit for auth attempts) ──

/// <summary>
/// In-memory IP attempt tracker for auth endpoints.
/// This is a fast first gate before DB lockout kicks in.
/// Backed by ConcurrentDictionary — resets on app restart.
/// For multi-instance deployments, replace with IDistributedCache.
/// </summary>
public sealed class IpLoginAttemptTracker
{
    private readonly record struct AttemptEntry(int Count, DateTime WindowStart);
    private readonly ConcurrentDictionary<string, AttemptEntry> _entries = new();

    private const int MaxPerWindow = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public bool IsBlocked(string ip)
    {
        if (!_entries.TryGetValue(ip, out var entry)) return false;
        if (DateTime.UtcNow - entry.WindowStart > Window)
        {
            _entries.TryRemove(ip, out _);
            return false;
        }
        return entry.Count >= MaxPerWindow;
    }

    public void RecordAttempt(string ip)
    {
        _entries.AddOrUpdate(
            ip,
            _ => new AttemptEntry(1, DateTime.UtcNow),
            (_, existing) =>
            {
                if (DateTime.UtcNow - existing.WindowStart > Window)
                    return new AttemptEntry(1, DateTime.UtcNow);
                return existing with { Count = existing.Count + 1 };
            });
    }

    public void Reset(string ip) => _entries.TryRemove(ip, out _);
}

// ─── Filter ───────────────────────────────────────────────────

public sealed class LoginLockoutFilter : IAsyncActionFilter
{
    private readonly IpLoginAttemptTracker _tracker;
    private readonly SecurityOptions _security;
    private readonly IConfiguration _config;
    private readonly ILogger<LoginLockoutFilter> _logger;

    public LoginLockoutFilter(
        IpLoginAttemptTracker tracker,
        IOptions<SecurityOptions> security,
        IConfiguration config,
        ILogger<LoginLockoutFilter> logger)
    {
        _tracker = tracker;
        _security = security.Value;
        _config = config;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var ip = GetClientIp(context.HttpContext);

        // ── Gate 1: IP-level block (in-memory, fast) ──────────────────────────
        if (_tracker.IsBlocked(ip))
        {
            _logger.LogWarning(
                "IP login rate limit exceeded. IP={IP}", ip);

            context.Result = new ObjectResult(
                ApiResult<object>.Fail(429, "IP_RATE_LIMIT",
                    "Too many login attempts from this IP. Try again in 15 minutes."))
            {
                StatusCode = 429
            };

            context.HttpContext.Response.Headers.Append("Retry-After", "900");
            return;
        }

        // ── Extract email from request body ──────────────────────────────────
        // The action already bound the model — we read it from action arguments
        string? email = null;
        if (context.ActionArguments.TryGetValue("request", out var reqObj)
            && reqObj is DTOs.LoginRequest loginReq)
        {
            email = loginReq.Email;
        }

        // ── Gate 2: DB-level account lockout check ────────────────────────────
        if (!string.IsNullOrEmpty(email))
        {
            var lockout = await CheckDbLockoutAsync(email);
            if (lockout.HasValue && lockout.Value > DateTime.UtcNow)
            {
                var retryAfterSeconds = (int)(lockout.Value - DateTime.UtcNow).TotalSeconds;

                _logger.LogWarning(
                    "Login blocked — account locked. Email={Email} Until={Until}",
                    email, lockout.Value);

                context.Result = new ObjectResult(
                    ApiResult<object>.Fail(423, "ACCOUNT_LOCKED",
                        "Account is temporarily locked due to too many failed attempts.",
                        $"Try again at {lockout.Value:HH:mm 'UTC'}."))
                {
                    StatusCode = 423
                };

                context.HttpContext.Response.Headers.Append(
                    "Retry-After", retryAfterSeconds.ToString());
                return;
            }
        }

        // ── Record IP attempt before executing ────────────────────────────────
        _tracker.RecordAttempt(ip);

        // ── Execute the action ────────────────────────────────────────────────
        var executed = await next();

        // ── On success: reset IP tracker ──────────────────────────────────────
        if (executed.Result is OkObjectResult)
            _tracker.Reset(ip);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private async Task<DateTime?> CheckDbLockoutAsync(string email)
    {
        try
        {
            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            var lockoutUntil = await conn.QuerySingleOrDefaultAsync<DateTime?>(
                """
                SELECT LockoutUntil
                FROM   dbo.Users
                WHERE  Email     = @Email
                  AND  IsDeleted = 0
                  AND  LockoutUntil IS NOT NULL
                  AND  LockoutUntil > SYSUTCDATETIME()
                """,
                new { Email = email });

            return lockoutUntil;
        }
        catch (Exception ex)
        {
            // Don't let DB failure block a legitimate login attempt
            _logger.LogError(ex, "Lockout check DB query failed");
            return null;
        }
    }

    private static string GetClientIp(Microsoft.AspNetCore.Http.HttpContext ctx)
    {
        var fwd = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(fwd))
            return fwd.Split(',')[0].Trim();
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}