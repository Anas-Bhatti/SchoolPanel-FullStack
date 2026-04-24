namespace SchoolPanel.Api.Models;

// ─── Auth / Identity ──────────────────────────────────────────────────────────

public sealed class User
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfilePhotoUrl { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }
    public string? GoogleId { get; set; }
    public string AuthProvider { get; set; } = "Local";
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; }
    public byte LoginAttempts { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIP { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class Role
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class RolePermission
{
    public int PermissionId { get; set; }
    public int RoleId { get; set; }
    public string Module { get; set; } = string.Empty;
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanExport { get; set; }
}

public sealed class RefreshToken
{
    public long TokenId { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public string? IPAddress { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }
}

// ─── Login SP result models ───────────────────────────────────────────────────

public sealed class LoginSpResult
{
    public string ResultCode { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? PasswordHash { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public byte LoginAttempts { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
}

public sealed class UserRoleResult
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
}