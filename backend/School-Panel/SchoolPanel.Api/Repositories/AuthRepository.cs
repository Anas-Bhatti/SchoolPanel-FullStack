using Dapper;
using Microsoft.Extensions.Options;
using SchoolPanel.Api.Configuration;
using SchoolPanel.Api.DTOs;
using SchoolPanel.Api.Models;
using SchoolPanel.Auth.DTOs;
using System.Data;
using System.Data.SqlClient;

namespace SchoolPanel.Api.Repositories;

public interface IAuthRepository
{
    Task<(LoginSpResult Result, IEnumerable<UserRoleResult> Roles,
          IEnumerable<Permission> Permissions)>
        LoginAsync(string email, string ip, CancellationToken ct = default);

    Task LoginSuccessAsync(Guid userId, string ip, CancellationToken ct = default);

    Task<(string ResultCode, int AttemptsRemaining, DateTime? LockoutUntil)>
        LoginFailedAsync(string email, string ip, int maxAttempts,
                         int lockoutMinutes, CancellationToken ct = default);

    Task SaveRefreshTokenAsync(Guid userId, string token, DateTime expiresAt,
        string ip, string? deviceInfo, string? oldToken, CancellationToken ct = default);

    Task<(RefreshToken? Token, User? User, IEnumerable<UserRoleResult> Roles)>
        ValidateRefreshTokenAsync(string token, CancellationToken ct = default);

    Task<int> RevokeAllUserTokensAsync(Guid userId, CancellationToken ct = default);

    Task<User?> GetUserByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetUserByIdAsync(Guid userId, CancellationToken ct = default);
    Task<User?> GetUserByGoogleIdAsync(string googleId, CancellationToken ct = default);

    Task UpdateTwoFactorSecretAsync(Guid userId, string? encryptedSecret,
        bool enabled, CancellationToken ct = default);

    Task<(string ResultCode, Guid? UserId)> CreateUserAsync(
        string email, string passwordHash, string fullName,
        string? phone, int roleId, Guid createdById, CancellationToken ct = default);
}

public sealed class AuthRepository : IAuthRepository
{
    private readonly string _connectionString;
    private readonly ILogger<AuthRepository> _logger;

    public AuthRepository(IConfiguration config, ILogger<AuthRepository> logger)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection string is missing.");
        _logger = logger;
    }

    private IDbConnection CreateConnection()
        => new SqlConnection(_connectionString);

    // ─── Login ────────────────────────────────────────────────────────────────
    public async Task<(LoginSpResult, IEnumerable<UserRoleResult>,
                       IEnumerable<Permission>)>
        LoginAsync(string email, string ip, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        conn.Open();

        var multi = await conn.QueryMultipleAsync(
            "dbo.sp_Login",
            new { Email = email, IPAddress = ip },
            commandType: CommandType.StoredProcedure);

        var result = await multi.ReadFirstOrDefaultAsync<LoginSpResult>();
        var roles = await multi.ReadAsync<UserRoleResult>();
        var permissions = await multi.ReadAsync<Permission>();

        return (result ?? new LoginSpResult { ResultCode = "USER_NOT_FOUND" },
                roles, permissions);
    }

    // ─── Login Success ────────────────────────────────────────────────────────
    public async Task LoginSuccessAsync(Guid userId, string ip, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "dbo.sp_LoginSuccess",
            new { UserId = userId, IPAddress = ip },
            commandType: CommandType.StoredProcedure);
    }

    // ─── Login Failed ─────────────────────────────────────────────────────────
    public async Task<(string ResultCode, int AttemptsRemaining, DateTime? LockoutUntil)>
        LoginFailedAsync(string email, string ip, int maxAttempts,
                         int lockoutMinutes, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_LoginFailed",
            new
            {
                Email = email,
                IPAddress = ip,
                MaxAttempts = maxAttempts,
                LockoutMinutes = lockoutMinutes
            },
            commandType: CommandType.StoredProcedure);

        if (row == null) return ("NOT_FOUND", 0, null);

        string code = row.ResultCode;
        int attempts = (int)(row.AttemptsRemaining ?? 0);
        DateTime? until = code == "LOCKED" ? (DateTime?)row.LockoutUntil : null;

        return (code, attempts, until);
    }

    // ─── Refresh Token ────────────────────────────────────────────────────────
    public async Task SaveRefreshTokenAsync(
        Guid userId, string token, DateTime expiresAt,
        string ip, string? deviceInfo, string? oldToken,
        CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "dbo.sp_SaveRefreshToken",
            new
            {
                UserId = userId,
                Token = token,
                ExpiresAt = expiresAt,
                IPAddress = ip,
                DeviceInfo = deviceInfo,
                OldToken = oldToken
            },
            commandType: CommandType.StoredProcedure);
    }

    public async Task<(RefreshToken? Token, User? User,
                       IEnumerable<UserRoleResult> Roles)>
        ValidateRefreshTokenAsync(string token, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var multi = await conn.QueryMultipleAsync(
            "dbo.sp_ValidateRefreshToken",
            new { Token = token },
            commandType: CommandType.StoredProcedure);

        var tokenData = await multi.ReadFirstOrDefaultAsync<dynamic>();
        var roles = await multi.ReadAsync<UserRoleResult>();

        if (tokenData == null) return (null, null, Enumerable.Empty<UserRoleResult>());

        var rt = new RefreshToken
        {
            IsRevoked = (bool)tokenData.IsRevoked,
            ExpiresAt = (DateTime)tokenData.ExpiresAt
        };

        var user = new User
        {
            UserId = (Guid)tokenData.UserId,
            Email = tokenData.Email,
            FullName = tokenData.FullName,
            IsActive = (bool)tokenData.IsActive,
            IsDeleted = (bool)tokenData.IsDeleted
        };

        return (rt, user, roles);
    }

    public async Task<int> RevokeAllUserTokensAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_RevokeAllUserTokens",
            new { UserId = userId },
            commandType: CommandType.StoredProcedure);
        return (int)(result?.TokensRevoked ?? 0);
    }

    // ─── User lookups ─────────────────────────────────────────────────────────
    public async Task<User?> GetUserByEmailAsync(string email, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM dbo.Users WHERE Email = @Email AND IsDeleted = 0",
            new { Email = email });
    }

    public async Task<User?> GetUserByIdAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM dbo.Users WHERE UserId = @UserId AND IsDeleted = 0",
            new { UserId = userId });
    }

    public async Task<User?> GetUserByGoogleIdAsync(
        string googleId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM dbo.Users WHERE GoogleId = @GoogleId AND IsDeleted = 0",
            new { GoogleId = googleId });
    }

    // ─── Two Factor ───────────────────────────────────────────────────────────
    public async Task UpdateTwoFactorSecretAsync(
        Guid userId, string? encryptedSecret, bool enabled,
        CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.ExecuteAsync(
            """
            UPDATE dbo.Users
            SET TwoFactorSecret  = @Secret,
                TwoFactorEnabled = @Enabled,
                UpdatedAt        = SYSUTCDATETIME()
            WHERE UserId = @UserId
            """,
            new { UserId = userId, Secret = encryptedSecret, Enabled = enabled });
    }

    // ─── Create User ──────────────────────────────────────────────────────────
    public async Task<(string ResultCode, Guid? UserId)> CreateUserAsync(
        string email, string passwordHash, string fullName,
        string? phone, int roleId, Guid createdById,
        CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "dbo.sp_CreateUser",
            new
            {
                Email = email,
                PasswordHash = passwordHash,
                FullName = fullName,
                PhoneNumber = phone,
                RoleId = roleId,
                CreatedById = createdById
            },
            commandType: CommandType.StoredProcedure);

        string code = result?.ResultCode ?? "ERROR";
        Guid? newId = result?.UserId;
        return (code, newId);
    }
}