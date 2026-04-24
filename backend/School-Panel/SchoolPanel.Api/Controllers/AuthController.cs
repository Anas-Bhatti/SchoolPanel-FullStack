// ============================================================
// Controllers/AuthController.cs
// All authentication endpoints — complete implementation.
//
// Endpoints:
//   POST /api/auth/login              — email + password
//   POST /api/auth/login/2fa-verify   — complete 2FA step
//   POST /api/auth/refresh            — rotate refresh token
//   POST /api/auth/logout             — revoke all sessions
//   POST /api/auth/google-login       — Google ID token
//   POST /api/auth/2fa/setup          — get QR code + recovery codes
//   POST /api/auth/2fa/verify-setup   — activate 2FA
//   POST /api/auth/2fa/disable        — disable 2FA
//   GET  /api/auth/me                 — current user info
// ============================================================

using BCrypt.Net;
using Google.Apis.Auth.OAuth2.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SchoolPanel.Auth.DTOs;
using SchoolPanel.Auth.Extensions;
using SchoolPanel.Auth.Filters;
using SchoolPanel.Auth.Services;
using System.Security.Claims;
using LoginRequest = SchoolPanel.Auth.DTOs.LoginRequest;
using RefreshTokenRequest = SchoolPanel.Auth.DTOs.RefreshTokenRequest;

namespace SchoolPanel.Auth.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly ITokenService _tokens;
    private readonly ITwoFactorService _twoFactor;
    private readonly IAuthDataService _data;
    private readonly IGoogleTokenVerifier _google;
    private readonly SecurityOptions _security;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ITokenService tokens,
        ITwoFactorService twoFactor,
        IAuthDataService data,
        IGoogleTokenVerifier google,
        IOptions<SecurityOptions> security,
        ILogger<AuthController> logger)
    {
        _tokens = tokens;
        _twoFactor = twoFactor;
        _data = data;
        _google = google;
        _security = security.Value;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/auth/login
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Authenticate with email + password.
    /// Returns full tokens on success, or RequiresTwoFactor=true + PendingToken
    /// when TOTP is enabled.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ServiceFilter(typeof(LoginLockoutFilter))]   // Handles IP + DB lockout
    [EnableRateLimiting("AuthLimit")]
    [ProducesResponseType(typeof(ApiResult<LoginResponse>), 200)]
    [ProducesResponseType(typeof(ApiResult<object>), 401)]
    [ProducesResponseType(typeof(ApiResult<object>), 423)]
    [ProducesResponseType(typeof(ApiResult<object>), 429)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var ip = GetClientIp();

        // ── 1. Call sp_Login ───────────────────────────────────────────────────
        var (spResult, roles, permissions) =
            await _data.SpLoginAsync(request.Email, ip, ct);

        // ── 2. Guard: user not found / inactive ────────────────────────────────
        // Use identical response for both — prevents user enumeration
        if (spResult.ResultCode is "USER_NOT_FOUND" or "ACCOUNT_INACTIVE")
        {
            await AuditAsync(null, request.Email, "LoginFailed", ip,
                isSuccess: false,
                errorMessage: $"ResultCode={spResult.ResultCode}");

            // Waste time equal to a BCrypt verify to prevent timing attacks
            BCrypt.Net.BCrypt.Verify("dummy", "$2a$12$Xhx2e2e2e2e2e2e2e2e2euhW4K7z");

            return Unauthorized(new { 
                status = 401, 
                code = "INVALID_CREDENTIALS", 
                message = "Invalid email or password." 
            });
        }

        // ── 3. Guard: sp handles lockout — but double-check result ─────────────
        if (spResult.ResultCode == "ACCOUNT_LOCKED")
        {
            await AuditAsync(null, request.Email, "AccountLocked", ip,
                isSuccess: false,
                errorMessage: $"Locked until {spResult.LockoutUntil}");

            return StatusCode(423, new {
                status = 423,
                code = "ACCOUNT_LOCKED",
                message = "Account locked due to too many failed attempts.",
                detail = $"Try again at {spResult.LockoutUntil:HH:mm UTC}."
            });
        }

        // ── 4. BCrypt password verification (in C#, not SQL) ──────────────────
        // BCrypt is intentionally slow — must run in C# layer
        var passwordValid = BCrypt.Net.BCrypt.Verify(
            request.Password,
            spResult.PasswordHash ?? string.Empty);

        if (!passwordValid)
        {
            var failResult = await _data.SpLoginFailedAsync(
                request.Email, ip,
                _security.MaxLoginAttempts,
                _security.LockoutMinutes, ct);

            await AuditAsync(spResult.UserId, request.Email, "LoginFailed", ip,
                isSuccess: false,
                errorMessage: $"Wrong password. Attempts remaining: {failResult.AttemptsRemaining}");

            if (failResult.ResultCode == "LOCKED")
            {
                _logger.LogWarning(
                    "Account locked after failed attempts. Email={Email} IP={IP}",
                    request.Email, ip);

                return StatusCode(423, new {
                    status = 423,
                    code = "ACCOUNT_LOCKED",
                    message = $"Account locked for {_security.LockoutMinutes} minutes.",
                    detail = $"Too many failed attempts. Locked until {failResult.LockoutUntil:HH:mm UTC}."
                });
            }

            return Unauthorized(new {
                status = 401,
                code = "INVALID_CREDENTIALS",
                message = "Invalid email or password.",
                detail = $"{failResult.AttemptsRemaining} attempt(s) remaining."
            });
        }

        var userId = spResult.UserId!.Value;
        var roleList = roles.Select(r => r.RoleName).ToArray();

        // ── 5. Clear login counter on success ─────────────────────────────────
        await _data.SpLoginSuccessAsync(userId, ip, ct);

        // ── 6. 2FA required path ───────────────────────────────────────────────
        if (spResult.TwoFactorEnabled)
        {
            // Issue a short-lived (5 min) pending JWT — no role/permission claims
            var pendingToken = _tokens.CreatePendingToken(userId, spResult.Email!);

            await AuditAsync(userId, spResult.Email, "LoginPending2FA", ip,
                isSuccess: true,
                errorMessage: null);

            _logger.LogInformation(
                "Login step 1 complete — awaiting 2FA. UserId={UserId}", userId);

            return Ok(new LoginResponse(
                AccessToken: null,
                RefreshToken: null,
                AccessTokenExpiry: _tokens.PendingTokenExpiry,
                RefreshTokenExpiry: DateTime.UtcNow,
                RequiresTwoFactor: true,
                PendingToken: pendingToken,
                User: null
            ));
        }

        // ── 7. Issue full token pair ───────────────────────────────────────────
        var permList = permissions.Select(p => new Permission(
            p.Module, p.CanView, p.CanCreate, p.CanEdit, p.CanDelete, p.CanExport))
            .ToList();

        var user = new AuthUser(
            userId, spResult.Email!, spResult.FullName ?? string.Empty,
            roleList, permList);

        return await IssueFullTokensAsync(user, permList, ip,
            request.DeviceInfo, null, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/auth/login/2fa-verify
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Complete the 2FA step using the PendingToken from /login.
    /// Also accepts a recovery code (8-char groups separated by hyphen).
    /// </summary>
    [HttpPost("login/2fa-verify")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimit")]
    [ProducesResponseType(typeof(ApiResult<LoginResponse>), 200)]
    [ProducesResponseType(typeof(ApiResult<object>), 401)]
    public async Task<IActionResult> VerifyTwoFactorLogin(
        [FromBody] TwoFactorVerifyRequest request,
        CancellationToken ct)
    {
        var ip = GetClientIp();

        // ── 1. Validate the pending JWT ────────────────────────────────────────
        var principal = _tokens.ValidateToken(request.PendingToken);
        if (principal == null)
        {
            return Unauthorized(new {
                status = 401,
                code = "INVALID_PENDING_TOKEN",
                message = "The 2FA session token is invalid or has expired. Please log in again."
            });
        }

        // ── 2. Ensure it IS a pending token ────────────────────────────────────
        if (!principal.HasClaim(TokenService.ClaimIsPending, "true"))
        {
            return Unauthorized(new {
                status = 401,
                code = "NOT_PENDING_TOKEN",
                message = "Invalid token type for 2FA verification."
            });
        }

        // ── 3. Extract userId ──────────────────────────────────────────────────
        var userIdStr = principal.FindFirstValue(TokenService.ClaimUserId)
                     ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized(new {
                status = 401,
                code = "INVALID_PENDING_TOKEN",
                message = "Invalid token claims."
            });
        }

        // ── 4. Verify TOTP code or recovery code ───────────────────────────────
        var isRecovery = request.Code.Contains('-') || request.Code.Length > 8;
        bool verified;

        if (isRecovery)
        {
            verified = await _twoFactor.VerifyRecoveryCodeAsync(userId, request.Code, ct);
            if (!verified)
            {
                await AuditAsync(userId, principal.FindFirstValue("email"),
                    "LoginFailed", ip, isSuccess: false,
                    errorMessage: "Invalid recovery code");

                return Unauthorized(new {
                    status = 401,
                    code = "INVALID_RECOVERY_CODE",
                    message = "Recovery code is invalid or already used."
                });
            }

            _logger.LogWarning(
                "Recovery code used at login. UserId={UserId}", userId);
        }
        else
        {
            verified = await _twoFactor.VerifyCodeAsync(userId, request.Code, ct);
            if (!verified)
            {
                await AuditAsync(userId, principal.FindFirstValue("email"),
                    "LoginFailed", ip, isSuccess: false,
                    errorMessage: "Invalid 2FA TOTP code");

                return Unauthorized(new {
                    status = 401,
                    code = "INVALID_2FA_CODE",
                    message = "Verification code is incorrect."
                });
            }
        }

        // ── 5. Load full user identity ─────────────────────────────────────────
        var (spResult, roles, permissions) =
            await _data.SpLoginAsync(
                principal.FindFirstValue(ClaimTypes.Email)
                ?? principal.FindFirstValue("email")
                ?? string.Empty,
                ip, ct);

        if (spResult.UserId == null || spResult.ResultCode != "VERIFY_PASSWORD")
        {
            // User deactivated between login steps — rare but possible
            return Unauthorized(new {
                status = 401,
                code = "ACCOUNT_UNAVAILABLE",
                message = "Account is no longer available."
            });
        }

        await _data.SpLoginSuccessAsync(userId, ip, ct);

        var roleList = roles.Select(r => r.RoleName).ToArray();
        var permList = permissions.Select(p => new Permission(
            p.Module, p.CanView, p.CanCreate, p.CanEdit, p.CanDelete, p.CanExport))
            .ToList();

        var user = new AuthUser(
            userId, spResult.Email!, spResult.FullName ?? string.Empty,
            roleList, permList);

        return await IssueFullTokensAsync(user, permList, ip,
            deviceInfo: null, oldRefreshToken: null, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/auth/refresh
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Rotate refresh token → new access + refresh pair.
    /// Old refresh token is revoked and its replacement recorded
    /// (ReplacedByToken column — enables token theft detection).
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("PerIp")]
    [ProducesResponseType(typeof(ApiResult<TokenPairResponse>), 200)]
    [ProducesResponseType(typeof(ApiResult<object>), 401)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken ct)
    {
        var ip = GetClientIp();

        // ── 1. Validate against DB ─────────────────────────────────────────────
        var (tokenRecord, roles) =
            await _tokens.ValidateRefreshTokenAsync(request.RefreshToken, ct);

        if (tokenRecord == null)
        {
            _logger.LogWarning(
                "Refresh token not found. IP={IP}", ip);
            return Unauthorized(new {
                status = 401,
                code = "INVALID_REFRESH_TOKEN",
                message = "Refresh token is invalid. Please log in again."
            });
        }

        // ── 2. Detect token reuse (theft indicator) ────────────────────────────
        if (tokenRecord.IsRevoked)
        {
            // A revoked token is being presented — possible token theft.
            // Nuclear response: revoke ALL sessions for this user.
            await _tokens.RevokeAllAsync(tokenRecord.UserId, ct);

            await AuditAsync(tokenRecord.UserId, tokenRecord.Email,
                "TokenTheftDetected", ip, isSuccess: false,
                errorMessage: "Revoked refresh token reuse detected — all sessions invalidated");

            _logger.LogWarning(
                "SECURITY: Revoked token reuse. UserId={UserId} IP={IP}. " +
                "All sessions revoked.", tokenRecord.UserId, ip);

            return Unauthorized(new {
                status = 401,
                code = "TOKEN_REUSE_DETECTED",
                message = "Security alert: your session has been terminated. Please log in again."
            });
        }

        // ── 3. Check expiry ────────────────────────────────────────────────────
        if (tokenRecord.ExpiresAt < DateTime.UtcNow)
        {
            return Unauthorized(new {
                status = 401,
                code = "REFRESH_TOKEN_EXPIRED",
                message = "Session has expired. Please log in again."
            });
        }

        // ── 4. Check account still active ─────────────────────────────────────
        if (!tokenRecord.IsActive || tokenRecord.IsDeleted)
        {
            return Unauthorized(new {
                status = 401,
                code = "ACCOUNT_INACTIVE",
                message = "Account is no longer active."
            });
        }

        // ── 5. Load fresh permissions (may have changed since last login) ──────
        var freshPerms = await _data.GetPermissionsAsync(tokenRecord.UserId, ct);
        var permList = freshPerms.Select(p => new Permission(
            p.Module, p.CanView, p.CanCreate, p.CanEdit, p.CanDelete, p.CanExport))
            .ToList();

        var roleList = roles.Select(r => r.RoleName).ToArray();

        var user = new AuthUser(
            tokenRecord.UserId,
            tokenRecord.Email,
            tokenRecord.FullName,
            roleList,
            permList);

        // ── 6. Issue new tokens (old one is rotated in sp_SaveRefreshToken) ────
        var newAccessToken = _tokens.CreateAccessToken(user, permList);
        var newRefreshToken = await _tokens.CreateRefreshTokenAsync(
            tokenRecord.UserId, ip,
            request.DeviceInfo,
            oldToken: request.RefreshToken,   // Triggers rotation in SP
            ct);

        _logger.LogDebug(
            "Token refreshed. UserId={UserId}", tokenRecord.UserId);

        return Ok(new TokenPairResponse(
            AccessToken: newAccessToken,
            RefreshToken: newRefreshToken,
            AccessTokenExpiry: _tokens.AccessTokenExpiry,
            RefreshTokenExpiry: _tokens.RefreshTokenExpiry
        ));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/auth/logout
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Revoke all refresh tokens for the current user (logout all devices).
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [EnableRateLimiting("PerUser")]
    [ProducesResponseType(typeof(ApiResult<object>), 200)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId.HasValue)
        {
            await _tokens.RevokeAllAsync(userId.Value, ct);
            await AuditAsync(userId, GetCurrentEmail(), "Logout", GetClientIp(),
                isSuccess: true);
            _logger.LogInformation("User logged out. UserId={UserId}", userId);
        }

        return Ok(new { message = "Logged out from all devices." });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/auth/google-login
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Authenticate with a Google ID token.
    /// Creates a new account automatically if no matching account exists.
    /// Links to an existing account if email already exists (same person, different provider).
    /// </summary>
    [HttpPost("google-login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimit")]
    [ProducesResponseType(typeof(ApiResult<LoginResponse>), 200)]
    [ProducesResponseType(typeof(ApiResult<object>), 401)]
    public async Task<IActionResult> GoogleLogin(
        [FromBody] GoogleLoginRequest request,
        CancellationToken ct)
    {
        var ip = GetClientIp();

        // ── 1. Verify ID token with Google ─────────────────────────────────────
        var googleUser = await _google.VerifyAsync(request.IdToken, ct);
        if (googleUser == null)
        {
            await AuditAsync(null, null, "LoginFailed", ip,
                isSuccess: false, errorMessage: "Invalid Google ID token");

            return Unauthorized(ApiResult<LoginResponse>.Fail(
                401, "INVALID_GOOGLE_TOKEN",
                "Google authentication failed. Invalid or expired token."));
        }

        if (!googleUser.EmailVerified)
        {
            return Unauthorized(ApiResult<LoginResponse>.Fail(
                401, "GOOGLE_EMAIL_UNVERIFIED",
                "Google account email address has not been verified."));
        }

        // ── 2. Find existing account ───────────────────────────────────────────
        var existingByGoogle = await _data.GetUserByGoogleIdAsync(googleUser.Subject, ct);
        var existingByEmail = existingByGoogle
                            ?? await _data.GetUserByEmailAsync(googleUser.Email, ct);

        Guid userId;

        if (existingByEmail != null)
        {
            // ── Case A: Account exists ─────────────────────────────────────────
            if (existingByEmail.AuthProvider != "Google")
            {
                // Email-password account — link Google ID automatically
                await _data.LinkGoogleAccountAsync(existingByEmail.UserId!.Value, googleUser.Subject, ct);
                _logger.LogInformation(
                    "Google account linked to existing user. UserId={UserId}",
                    existingByEmail.UserId);
            }

            // Check account not deactivated
            if (existingByEmail.AuthProvider == "Local"
                && !(existingByEmail.ResultCode?.Equals("VERIFY_PASSWORD") ?? false))
            {
                // Re-read to get IsActive
            }

            userId = existingByEmail.UserId!.Value;
        }
        else
        {
            // ── Case B: New user — auto-register ──────────────────────────────
            var (code, newId) = await _data.CreateGoogleUserAsync(
                googleUser.Email, googleUser.Name,
                googleUser.Subject, googleUser.PictureUrl, ct);

            if (code != "SUCCESS" || newId == null)
            {
                _logger.LogError(
                    "Google auto-register failed. Email={Email} Code={Code}",
                    googleUser.Email, code);

            return StatusCode(500, new {
                status = 500,
                code = "REGISTRATION_FAILED",
                message = "Failed to create account. Please contact support."
            });
            }

            userId = newId.Value;
            _logger.LogInformation(
                "New user auto-registered via Google. UserId={UserId} Email={Email}",
                userId, googleUser.Email);
        }

        // ── 3. Load full identity ──────────────────────────────────────────────
        var (spResult, roles, permissions) =
            await _data.SpLoginAsync(googleUser.Email, ip, ct);

        if (spResult.ResultCode == "ACCOUNT_INACTIVE")
        {
            return Unauthorized(new {
                status = 401,
                code = "ACCOUNT_INACTIVE",
                message = "Account is inactive."
            });
        }

        var roleList = roles.Select(r => r.RoleName).ToArray();
        var permList = permissions.Select(p => new Permission(
            p.Module, p.CanView, p.CanCreate, p.CanEdit, p.CanDelete, p.CanExport))
            .ToList();

        var user = new AuthUser(
            userId, googleUser.Email, googleUser.Name, roleList, permList);

        await AuditAsync(userId, googleUser.Email, "Login", ip,
            isSuccess: true,
            errorMessage: null,
            description: "Google OAuth login");

        return await IssueFullTokensAsync(user, permList, ip,
            request.DeviceInfo, null, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/auth/2fa/setup
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Generate TOTP secret + QR URI + recovery codes.
    /// 2FA is NOT yet active — user must call /2fa/verify-setup to activate.
    /// </summary>
    [HttpPost("2fa/setup")]
    [Authorize]
    [EnableRateLimiting("PerUser")]
    [ProducesResponseType(typeof(ApiResult<TwoFactorSetupResponse>), 200)]
    public async Task<IActionResult> SetupTwoFactor(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var email = GetCurrentEmail();

        if (userId == null) return Unauthorized();

        var (secret, qrUri, manual, recovery) =
            await _twoFactor.GenerateSetupAsync(userId.Value, email!, ct);

        await AuditAsync(userId, email, "TwoFactorSetup", GetClientIp(),
            isSuccess: true,
            description: "2FA setup initiated — pending activation");

        return Ok(new TwoFactorSetupResponse(
            SecretKey: secret,
            QrCodeUri: qrUri,
            ManualEntryKey: manual,
            RecoveryCodes: recovery
        ));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/auth/2fa/verify-setup
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Verify the first TOTP code after scanning QR — activates 2FA permanently.
    /// </summary>
    [HttpPost("2fa/verify-setup")]
    [Authorize]
    [EnableRateLimiting("PerUser")]
    [ProducesResponseType(typeof(ApiResult<object>), 200)]
    [ProducesResponseType(typeof(ApiResult<object>), 400)]
    public async Task<IActionResult> VerifyTwoFactorSetup(
        [FromBody] Setup2FaVerifyRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var email = GetCurrentEmail();

        if (userId == null) return Unauthorized();

        var valid = await _twoFactor.VerifyCodeAsync(userId.Value, request.Code, ct);
        if (!valid)
        {
            await AuditAsync(userId, email, "TwoFactorSetup", GetClientIp(),
                isSuccess: false, errorMessage: "Invalid TOTP code during setup");

            return BadRequest(new {
                status = 400,
                code = "INVALID_CODE",
                message = "Verification code is incorrect. Check your authenticator app and try again."
            });
        }

        await _twoFactor.EnableAsync(userId.Value, ct);

        await AuditAsync(userId, email, "TwoFactorEnabled", GetClientIp(),
            isSuccess: true, description: "2FA activated");

        _logger.LogInformation("2FA activated for UserId={UserId}", userId);

        return Ok(new
        {
            message = "Two-factor authentication is now active on your account."
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // POST /api/auth/2fa/disable
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Disable 2FA after verifying current TOTP code.
    /// Wipes secret and all recovery codes.
    /// </summary>
    [HttpPost("2fa/disable")]
    [Authorize]
    [EnableRateLimiting("PerUser")]
    [ProducesResponseType(typeof(ApiResult<object>), 200)]
    [ProducesResponseType(typeof(ApiResult<object>), 400)]
    public async Task<IActionResult> DisableTwoFactor(
        [FromBody] Disable2FaRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var email = GetCurrentEmail();

        if (userId == null) return Unauthorized();

        // Must verify current code before disabling (confirm identity)
        var valid = await _twoFactor.VerifyCodeAsync(userId.Value, request.Code, ct);
        if (!valid)
        {
            // Also accept a recovery code here
            valid = await _twoFactor.VerifyRecoveryCodeAsync(
                userId.Value, request.Code, ct);
        }

        if (!valid)
        {
            await AuditAsync(userId, email, "TwoFactorDisable", GetClientIp(),
                isSuccess: false, errorMessage: "Invalid code — disable blocked");

            return BadRequest(new {
                status = 400,
                code = "INVALID_CODE",
                message = "Verification failed. 2FA has NOT been disabled."
            });
        }

        await _twoFactor.DisableAsync(userId.Value, ct);

        await AuditAsync(userId, email, "TwoFactorDisabled", GetClientIp(),
            isSuccess: true, description: "2FA disabled by user");

        _logger.LogInformation("2FA disabled for UserId={UserId}", userId);

        return Ok(new
        {
            message = "Two-factor authentication has been disabled."
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GET /api/auth/me
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Return current authenticated user's identity and permissions from JWT claims.
    /// No DB call — reads directly from token.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [EnableRateLimiting("PerUser")]
    [ProducesResponseType(typeof(ApiResult<AuthUser>), 200)]
    public IActionResult Me()
    {
        var userId = GetCurrentUserId();
        var email = GetCurrentEmail() ?? string.Empty;
        var fullName = User.FindFirstValue(ClaimTypes.Name)
                    ?? User.FindFirstValue("name")
                    ?? string.Empty;
        var roles = User.FindAll(ClaimTypes.Role)
                          .Select(c => c.Value)
                          .ToArray();

        // Parse permission claims from JWT
        var permissions = User.FindAll(TokenService.ClaimPermission)
            .Select(c =>
            {
                var parts = c.Value.Split(':', 2);
                if (parts.Length != 2 || parts[1].Length != 5)
                    return null;
                var m = parts[1];
                return new Permission(
                    Module: parts[0],
                    CanView: m[0] == '1',
                    CanCreate: m[1] == '1',
                    CanEdit: m[2] == '1',
                    CanDelete: m[3] == '1',
                    CanExport: m[4] == '1');
            })
            .Where(p => p != null)
            .Cast<Permission>()
            .ToList();

        if (userId == null) return Unauthorized();

        return Ok(new AuthUser(
            userId.Value, email, fullName, roles, permissions));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<IActionResult> IssueFullTokensAsync(
        AuthUser user,
        IReadOnlyList<Permission> permissions,
        string ip,
        string? deviceInfo,
        string? oldRefreshToken,
        CancellationToken ct)
    {
        var accessToken = _tokens.CreateAccessToken(user, permissions);
        var refreshToken = await _tokens.CreateRefreshTokenAsync(
            user.UserId, ip, deviceInfo, oldRefreshToken, ct);

        await AuditAsync(user.UserId, user.Email, "Login", ip,
            isSuccess: true,
            description: $"Login successful. Roles: {string.Join(", ", user.Roles)}");

        _logger.LogInformation(
            "Login successful. UserId={UserId} Roles={Roles}",
            user.UserId, string.Join(",", user.Roles));

        return Ok(new LoginResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            AccessTokenExpiry: _tokens.AccessTokenExpiry,
            RefreshTokenExpiry: _tokens.RefreshTokenExpiry,
            RequiresTwoFactor: false,
            PendingToken: null,
            User: user
        ));
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(TokenService.ClaimUserId)
                 ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private string? GetCurrentEmail()
        => User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("email");

    private string GetClientIp()
    {
        var fwd = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(fwd))
            return fwd.Split(',')[0].Trim();
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private Task AuditAsync(
        Guid? userId, string? email, string action, string ip,
        bool isSuccess, string? errorMessage = null, string? description = null)
        => _data.InsertAuditLogAsync(
            userId, email, action, "Auth",
            recordId: userId?.ToString(),
            description: description ?? $"{action} from {ip}",
            ipAddress: ip,
            userAgent: Request.Headers["User-Agent"].ToString(),
            isSuccess: isSuccess,
            errorMessage: errorMessage);
}