// ============================================================
// Services/AuthDataService.cs
// All database operations needed by the auth layer.
// Calls: sp_Login, sp_LoginSuccess, sp_LoginFailed,
//        sp_GetUserPermissions, sp_InsertAuditLog,
//        sp_ValidateRefreshToken, sp_SaveRefreshToken,
//        sp_RevokeAllUserTokens
// ============================================================

using System.Data;
using System.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolPanel.Auth.DTOs;
using SchoolPanel.Auth.Models;

namespace SchoolPanel.Auth.Services;

public interface IAuthDataService
{
    // ── Login flow ────────────────────────────────────────────
    Task<(SpLoginResult Result,
          IEnumerable<SpRoleResult> Roles,
          IEnumerable<SpPermissionResult> Permissions)>
        SpLoginAsync(string email, string ip, CancellationToken ct = default);

    Task SpLoginSuccessAsync(Guid userId, string ip, CancellationToken ct = default);

    Task<LoginFailedResult> SpLoginFailedAsync(
        string email, string ip, int maxAttempts, int lockoutMinutes,
        CancellationToken ct = default);

    // ── Permissions ───────────────────────────────────────────
    Task<IEnumerable<SpPermissionResult>> GetPermissionsAsync(
        Guid userId, CancellationToken ct = default);

    // ── User lookups ──────────────────────────────────────────
    Task<SpLoginResult?> GetUserByEmailAsync(
        string email, CancellationToken ct = default);

    Task<SpLoginResult?> GetUserByGoogleIdAsync(
        string googleId, CancellationToken ct = default);

    Task<(string ResultCode, Guid? UserId)> CreateGoogleUserAsync(
        string email, string fullName, string googleId,
        string? pictureUrl, CancellationToken ct = default);

    Task LinkGoogleAccountAsync(
        Guid userId, string googleId, CancellationToken ct = default);

    // ── Audit ─────────────────────────────────────────────────
    Task InsertAuditLogAsync(
        Guid? userId,
        string? userEmail,
        string action,
        string module,
        string? recordId,
        string? description,
        string ipAddress,
        string? userAgent,
        bool isSuccess,
        string? errorMessage = null,
        CancellationToken ct = default);
}

public sealed class AuthDataService : IAuthDataService
{
    private readonly IConfiguration _config;
    private readonly ILogger<AuthDataService> _logger;

    public AuthDataService(IConfiguration config, ILogger<AuthDataService> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // sp_Login → 3 result sets: result, roles, permissions
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<(SpLoginResult Result,
                        IEnumerable<SpRoleResult> Roles,
                        IEnumerable<SpPermissionResult> Permissions)>
        SpLoginAsync(string email, string ip, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_Login",
            new { Email = email, IPAddress = ip },
            commandType: CommandType.StoredProcedure);

        var result = await multi.ReadFirstOrDefaultAsync<SpLoginResult>()
                  ?? new SpLoginResult { ResultCode = "USER_NOT_FOUND" };

        var roles = Enumerable.Empty<SpRoleResult>();
        var perms = Enumerable.Empty<SpPermissionResult>();

        // Only read additional result sets if credentials exist to verify
        if (result.ResultCode == "VERIFY_PASSWORD")
        {
            roles = await multi.ReadAsync<SpRoleResult>();
            perms = await multi.ReadAsync<SpPermissionResult>();
        }

        return (result, roles, perms);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // sp_LoginSuccess — clear attempts, update LastLoginAt
    // ─────────────────────────────────────────────────────────────────────────
    public async Task SpLoginSuccessAsync(
        Guid userId, string ip, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(
            "dbo.sp_LoginSuccess",
            new { UserId = userId, IPAddress = ip },
            commandType: CommandType.StoredProcedure);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // sp_LoginFailed — increment counter, lock if threshold hit
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<LoginFailedResult> SpLoginFailedAsync(
        string email, string ip, int maxAttempts, int lockoutMinutes,
        CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_LoginFailed",
            new
            {
                Email = email,
                IPAddress = ip,
                MaxAttempts = (byte)maxAttempts,
                LockoutMinutes = lockoutMinutes
            },
            commandType: CommandType.StoredProcedure);

        if (row == null)
            return new LoginFailedResult { ResultCode = "NOT_FOUND" };

        return new LoginFailedResult
        {
            ResultCode = (string)row.ResultCode,
            AttemptsRemaining = (int)(row.AttemptsRemaining ?? 0),
            LockoutUntil = row.ResultCode == "LOCKED"
                                ? (DateTime?)row.LockoutUntil : null
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // sp_GetUserPermissions — fresh permission load (used in handler)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<IEnumerable<SpPermissionResult>> GetPermissionsAsync(
        Guid userId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        return await conn.QueryAsync<SpPermissionResult>(
            "dbo.sp_GetUserPermissions",
            new { UserId = userId },
            commandType: CommandType.StoredProcedure);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // User lookups for Google OAuth
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<SpLoginResult?> GetUserByEmailAsync(
        string email, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        return await conn.QueryFirstOrDefaultAsync<SpLoginResult>(
            """
            SELECT UserId, Email, FullName, TwoFactorEnabled,
                   IsActive, IsDeleted, AuthProvider, GoogleId,
                   LoginAttempts, LockoutUntil
            FROM   dbo.Users
            WHERE  Email = @Email AND IsDeleted = 0
            """,
            new { Email = email });
    }

    public async Task<SpLoginResult?> GetUserByGoogleIdAsync(
        string googleId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        return await conn.QueryFirstOrDefaultAsync<SpLoginResult>(
            """
            SELECT UserId, Email, FullName, TwoFactorEnabled,
                   IsActive, IsDeleted, AuthProvider, GoogleId,
                   LoginAttempts, LockoutUntil
            FROM   dbo.Users
            WHERE  GoogleId = @GoogleId AND IsDeleted = 0
            """,
            new { GoogleId = googleId });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Auto-register Google user (Student role = 4 by default)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<(string ResultCode, Guid? UserId)> CreateGoogleUserAsync(
        string email, string fullName, string googleId,
        string? pictureUrl, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Random unusable password — Google users cannot use password login
        var unusableHash = BCrypt.Net.BCrypt.HashPassword(
            Guid.NewGuid().ToString("N"),
            workFactor: 4);  // Low factor — this hash is never used for verification

        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_CreateUser",
            new
            {
                Email = email,
                PasswordHash = unusableHash,
                FullName = fullName,
                PhoneNumber = (string?)null,
                RoleId = 4,               // Student — override per business logic
                CreatedById = Guid.Empty,
                GoogleId = googleId,
                AuthProvider = "Google",
                ProfilePhotoUrl = pictureUrl,
                IsEmailVerified = true             // Google already verified it
            },
            commandType: CommandType.StoredProcedure);

        string code = row?.ResultCode ?? "ERROR";
        Guid? newId = row?.UserId;
        return (code, newId);
    }

    public async Task LinkGoogleAccountAsync(
        Guid userId, string googleId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(
            """
            UPDATE dbo.Users
            SET    GoogleId     = @GoogleId,
                   AuthProvider = 'Google',
                   UpdatedAt    = SYSUTCDATETIME()
            WHERE  UserId = @UserId AND IsDeleted = 0
            """,
            new { UserId = userId, GoogleId = googleId });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // sp_InsertAuditLog
    // ─────────────────────────────────────────────────────────────────────────
    public async Task InsertAuditLogAsync(
        Guid? userId,
        string? userEmail,
        string action,
        string module,
        string? recordId,
        string? description,
        string ipAddress,
        string? userAgent,
        bool isSuccess,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        try
        {
            using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            await conn.ExecuteAsync(
                "dbo.sp_InsertAuditLog",
                new
                {
                    UserId = userId,
                    UserEmail = userEmail,
                    Action = action,
                    Module = module,
                    RecordId = recordId,
                    OldValue = (string?)null,
                    NewValue = (string?)null,
                    Description = description,
                    IPAddress = ipAddress,
                    UserAgent = userAgent,
                    IsSuccess = isSuccess,
                    ErrorMessage = errorMessage
                },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            // Audit log failure must NEVER crash the request
            _logger.LogError(ex, "Failed to write audit log. Action={Action}", action);
        }
    }

    private SqlConnection CreateConnection()
        => new(_config.GetConnectionString("DefaultConnection")
               ?? throw new InvalidOperationException("DefaultConnection missing."));
}