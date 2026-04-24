// ============================================================
// DTOs/AuthDtos.cs
// Request & response contracts for all auth endpoints
// ============================================================

using System.ComponentModel.DataAnnotations;

namespace SchoolPanel.Auth.DTOs;

// ─── Requests ─────────────────────────────────────────────────

public sealed record LoginRequest(
    [Required, EmailAddress, MaxLength(256)]
    string Email,

    [Required, MinLength(8), MaxLength(128)]
    string Password,

    /// <summary>Optional: device/browser fingerprint for token tracking</summary>
    string? DeviceInfo = null
);

public sealed record TwoFactorVerifyRequest(
    /// <summary>Short-lived temp JWT issued when 2FA is required</summary>
    [Required]
    string PendingToken,

    /// <summary>6-digit TOTP code from authenticator app</summary>
    [Required, StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be exactly 6 digits")]
    string Code
);

public sealed record RefreshTokenRequest(
    [Required]
    string RefreshToken,

    string? DeviceInfo = null
);

public sealed record GoogleLoginRequest(
    /// <summary>ID token from Google Sign-In (frontend passes this)</summary>
    [Required]
    string IdToken,

    string? DeviceInfo = null
);

public sealed record Setup2FaVerifyRequest(
    /// <summary>First TOTP code after scanning QR — activates 2FA</summary>
    [Required, StringLength(6, MinimumLength = 6)]
    string Code
);

public sealed record Disable2FaRequest(
    /// <summary>Current TOTP code to confirm identity before disabling</summary>
    [Required, StringLength(6, MinimumLength = 6)]
    string Code
);

public sealed record ChangePasswordRequest(
    [Required]
    string CurrentPassword,

    [Required, MinLength(8), MaxLength(128)]
    string NewPassword,

    [Required]
    [property: Compare("NewPassword")]
    string ConfirmNewPassword
);

// ─── Responses ────────────────────────────────────────────────

public sealed record LoginResponse(
    /// <summary>null when RequiresTwoFactor = true</summary>
    string? AccessToken,
    /// <summary>null when RequiresTwoFactor = true</summary>
    string? RefreshToken,
    DateTime AccessTokenExpiry,
    DateTime RefreshTokenExpiry,
    bool RequiresTwoFactor,
    /// <summary>Short-lived JWT valid for 2FA step only; null on normal login</summary>
    string? PendingToken,
    AuthUser? User
);

public sealed record AuthUser(
    Guid UserId,
    string Email,
    string FullName,
    string[] Roles,
    IReadOnlyList<Permission> Permissions
);

public sealed record Permission(
    string Module,
    bool CanView,
    bool CanCreate,
    bool CanEdit,
    bool CanDelete,
    bool CanExport
);

public sealed record TokenPairResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiry,
    DateTime RefreshTokenExpiry
);

public sealed record TwoFactorSetupResponse(
    /// <summary>Raw Base32 secret — only show once</summary>
    string SecretKey,
    /// <summary>otpauth:// URI — encode as QR on frontend</summary>
    string QrCodeUri,
    /// <summary>Space-separated groups of 4 for manual entry</summary>
    string ManualEntryKey,
    /// <summary>Recovery codes (8 × 16-char alphanumeric)</summary>
    IReadOnlyList<string> RecoveryCodes
);

public sealed record ApiError(
    int Status,
    string Code,
    string Message,
    string? Detail = null
);

public sealed record ApiResult<T>(
    bool Success,
    T? Data,
    ApiError? Error
)
{
    public static ApiResult<T> Ok(T data) => new(true, data, null);
    public static ApiResult<T> Fail(ApiError err) => new(false, default, err);
    public static ApiResult<T> Fail(int status,
        string code, string message, string? detail = null)
        => Fail(new ApiError(status, code, message, detail));
}