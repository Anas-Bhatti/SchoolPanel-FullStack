// ============================================================
// Models/AuthModels.cs
// Domain models — map 1:1 to DB tables and SP results
// ============================================================

namespace SchoolPanel.Auth.Models;

// ─── SP Result Models ─────────────────────────────────────────

/// <summary>
/// First result set from sp_Login
/// </summary>
public sealed class SpLoginResult
{
    public string ResultCode { get; init; } = string.Empty;
    // VERIFY_PASSWORD | USER_NOT_FOUND | ACCOUNT_INACTIVE | ACCOUNT_LOCKED
    public Guid? UserId { get; init; }
    public string? PasswordHash { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public byte LoginAttempts { get; init; }
    public DateTime? LockoutUntil { get; init; }
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string AuthProvider { get; init; } = "Local";
}

/// <summary>
/// Role row returned by sp_Login (second result set)
/// </summary>
public sealed class SpRoleResult
{
    public int RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
}

/// <summary>
/// Permission row from sp_GetUserPermissions
/// </summary>
public sealed class SpPermissionResult
{
    public string Module { get; init; } = string.Empty;
    public bool CanView { get; init; }
    public bool CanCreate { get; init; }
    public bool CanEdit { get; init; }
    public bool CanDelete { get; init; }
    public bool CanExport { get; init; }
}

/// <summary>
/// Stored refresh token record
/// </summary>
public sealed class RefreshTokenRecord
{
    public long TokenId { get; init; }
    public Guid UserId { get; init; }
    public string Token { get; init; } = string.Empty;
    public bool IsRevoked { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime IssuedAt { get; init; }
    public string? ReplacedByToken { get; init; }
    public string? DeviceInfo { get; init; }
    public string? IPAddress { get; init; }
}

/// <summary>
/// Minimal user info needed for token re-issue (from sp_ValidateRefreshToken)
/// </summary>
public sealed class RefreshTokenValidationResult
{
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsRevoked { get; init; }
    public DateTime ExpiresAt { get; init; }
}

/// <summary>
/// Result from sp_LoginFailed
/// </summary>
public sealed class LoginFailedResult
{
    public string ResultCode { get; init; } = string.Empty;
    // FAILED | LOCKED
    public int AttemptsRemaining { get; init; }
    public DateTime? LockoutUntil { get; init; }
}