// ============================================================
// Services/TokenService.cs
// JWT access token creation + refresh token lifecycle
// Uses: Microsoft.IdentityModel.Tokens, System.IdentityModel.Tokens.Jwt
// ============================================================

using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SchoolPanel.Auth.DTOs;
using SchoolPanel.Auth.Models;
using static Microsoft.Extensions.Logging.LoggerMessage;

namespace SchoolPanel.Auth.Services;

// ─── Options ──────────────────────────────────────────────────

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; init; } = "schoolpanel";
    public string Audience { get; init; } = "schoolpanel-api";
    public string SecretKey { get; init; } = string.Empty;
    public int AccessTokenExpiryMinutes { get; init; } = 15;
    public int RefreshTokenExpiryDays { get; init; } = 7;
    /// <summary>Short-lived JWT for 2FA pending step (minutes)</summary>
    public int PendingTokenExpiryMinutes { get; init; } = 5;
}

// ─── Interface ────────────────────────────────────────────────

public interface ITokenService
{
    /// <summary>
    /// Issue a signed HS512 JWT access token containing userId, email,
    /// roles, and module-level permission claims.
    /// </summary>
    string CreateAccessToken(
        AuthUser user,
        IEnumerable<Permission> permissions);

    /// <summary>
    /// Issue a short-lived "pending" JWT used only during the 2FA step.
    /// Contains userId + email but NO role/permission claims.
    /// </summary>
    string CreatePendingToken(Guid userId, string email);

    /// <summary>
    /// Generate a cryptographically random 64-byte refresh token,
    /// persist it in RefreshTokens via sp_SaveRefreshToken,
    /// and optionally revoke the token it rotates.
    /// </summary>
    Task<string> CreateRefreshTokenAsync(
        Guid userId,
        string ipAddress,
        string? deviceInfo,
        string? oldToken,
        CancellationToken ct = default);

    /// <summary>
    /// Validate a refresh token: check existence, revocation, expiry.
    /// Returns null on any failure.
    /// </summary>
    Task<(RefreshTokenValidationResult? Token,
          IEnumerable<SpRoleResult> Roles)>
        ValidateRefreshTokenAsync(
            string token, CancellationToken ct = default);

    /// <summary>
    /// Revoke all refresh tokens for a user (logout from all devices).
    /// </summary>
    Task RevokeAllAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Validate a JWT without lifetime enforcement (used during refresh).
    /// Returns null if signature/claims are invalid.
    /// </summary>
    ClaimsPrincipal? ReadExpiredToken(string token);

    /// <summary>
    /// Validate a JWT with full lifetime enforcement.
    /// Returns null if token is invalid or expired.
    /// </summary>
    ClaimsPrincipal? ValidateToken(string token);

    DateTime AccessTokenExpiry { get; }
    DateTime RefreshTokenExpiry { get; }
    DateTime PendingTokenExpiry { get; }
}

// ─── Implementation ───────────────────────────────────────────

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _jwt;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly IConfiguration _config;
    private readonly ILogger<TokenService> _logger;

    // Custom claim type constants — avoids magic strings throughout codebase
    public const string ClaimUserId = "uid";
    public const string ClaimPermission = "perm";  // "Module:VCEDE" bitmask string
    public const string ClaimIsPending = "pending";

    // LoggerMessage delegate for improved performance
    private static readonly Action<ILogger, Guid, string, DateTime, Exception?> _logRefreshTokenIssued =
        LoggerMessage.Define<Guid, string, DateTime>(
            LogLevel.Debug,
            default,
            "Refresh token issued. UserId={UserId} IP={IP} Expires={Exp}");

    private static readonly Action<ILogger, Exception?> _logRefreshTokenNotFound =
        LoggerMessage.Define(
            LogLevel.Warning,
            default,
            "Refresh token not found in database.");

    private static readonly Action<ILogger, int, Guid, Exception?> _logRefreshTokenRevoked =
        LoggerMessage.Define<int, Guid>(
            LogLevel.Information,
            default,
            "Revoked {Count} refresh tokens for UserId={UserId}");

    private static readonly Action<ILogger, Exception?> _logJwtValidationFailed =
        LoggerMessage.Define(
            LogLevel.Debug,
            default,
            "JWT validation failed");

    public TokenService(
        IOptions<JwtOptions> jwtOptions,
        IConfiguration config,
        ILogger<TokenService> logger)
    {
        _jwt = jwtOptions.Value;
        _config = config;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_jwt.SecretKey) || _jwt.SecretKey.Length < 64)
            throw new InvalidOperationException(
                "Jwt:SecretKey must be ≥64 characters for HS512.");

        _signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwt.SecretKey));
    }

    // ─── Expiry helpers ───────────────────────────────────────
    public DateTime AccessTokenExpiry
        => DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpiryMinutes);

    public DateTime RefreshTokenExpiry
        => DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays);

    public DateTime PendingTokenExpiry
        => DateTime.UtcNow.AddMinutes(_jwt.PendingTokenExpiryMinutes);

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Access Token
    // ─────────────────────────────────────────────────────────────────────────
    public string CreateAccessToken(
        AuthUser user,
        IEnumerable<Permission> permissions)
    {
        var expiry = AccessTokenExpiry;
        var claims = BuildAccessClaims(user, permissions, expiry);
        return WriteToken(claims, expiry);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Pending Token (2FA gate)
    // ─────────────────────────────────────────────────────────────────────────
    public string CreatePendingToken(Guid userId, string email)
    {
        var expiry = PendingTokenExpiry;

        var claims = new List<Claim>
        {
            new(ClaimUserId,                    userId.ToString()),
            new(ClaimTypes.NameIdentifier,      userId.ToString()),
            new(JwtRegisteredClaimNames.Sub,    userId.ToString()),
            new(JwtRegisteredClaimNames.Email,  email),
            new(JwtRegisteredClaimNames.Jti,    Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            // Flag that marks this as a limited-purpose token
            new(ClaimIsPending, "true")
        };

        return WriteToken(claims, expiry);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Refresh Token — create, persist, rotate
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<string> CreateRefreshTokenAsync(
        Guid userId,
        string ipAddress,
        string? deviceInfo,
        string? oldToken,
        CancellationToken ct = default)
    {
        // 64 bytes = 512 bits of entropy — unguessable, URL-safe Base64
        var tokenBytes = new byte[64];
        RandomNumberGenerator.Fill(tokenBytes);
        var newToken = Convert.ToBase64String(tokenBytes)
                              .Replace('+', '-')
                              .Replace('/', '_')
                              .TrimEnd('=');

        var expiresAt = RefreshTokenExpiry;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(
            "dbo.sp_SaveRefreshToken",
            new
            {
                UserId = userId,
                Token = newToken,
                ExpiresAt = expiresAt,
                IPAddress = ipAddress,
                DeviceInfo = deviceInfo,
                OldToken = oldToken   // sp uses this to revoke+record ReplacedByToken
            },
            commandType: CommandType.StoredProcedure);

        _logRefreshTokenIssued(_logger, userId, ipAddress, expiresAt, null);

        return newToken;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Validate Refresh Token
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<(RefreshTokenValidationResult? Token,
                       IEnumerable<SpRoleResult> Roles)>
        ValidateRefreshTokenAsync(string token, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // sp returns: user row + roles (2 result sets)
        using var multi = await conn.QueryMultipleAsync(
            "dbo.sp_ValidateRefreshToken",
            new { Token = token },
            commandType: CommandType.StoredProcedure);

        var record = await multi.ReadFirstOrDefaultAsync<RefreshTokenValidationResult>();
        var roles = await multi.ReadAsync<SpRoleResult>();

        if (record == null)
        {
            _logRefreshTokenNotFound(_logger, null);
            return (null, Enumerable.Empty<SpRoleResult>());
        }

        return (record, roles);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Revoke All — logout everywhere
    // ─────────────────────────────────────────────────────────────────────────
    public async Task RevokeAllAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var revoked = await conn.QuerySingleAsync<int>(
            "dbo.sp_RevokeAllUserTokens",
            new { UserId = userId },
            commandType: CommandType.StoredProcedure);

        _logRefreshTokenRevoked(_logger, revoked, userId, null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Read/Validate JWT
    // ─────────────────────────────────────────────────────────────────────────
    public ClaimsPrincipal? ReadExpiredToken(string token)
        => InternalValidate(token, validateLifetime: false);

    public ClaimsPrincipal? ValidateToken(string token)
        => InternalValidate(token, validateLifetime: true);

    // ─── Private helpers ──────────────────────────────────────

    private string WriteToken(IEnumerable<Claim> claims, DateTime expiry)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            NotBefore = DateTime.UtcNow,
            IssuedAt = DateTime.UtcNow,
            Expires = expiry,
            SigningCredentials = new SigningCredentials(
                _signingKey,
                SecurityAlgorithms.HmacSha512Signature)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static List<Claim> BuildAccessClaims(
        AuthUser user,
        IEnumerable<Permission> permissions,
        DateTime expiry)
    {
        var claims = new List<Claim>
        {
            // Standard JWT claims
            new(ClaimUserId,                   user.UserId.ToString()),
            new(ClaimTypes.NameIdentifier,     user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Sub,   user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name,  user.FullName),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        };

        // Roles — one claim per role so ClaimTypes.Role array works
        foreach (var role in user.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        // Permissions — compact bitmask: "Students:11101"
        // Position: View|Create|Edit|Delete|Export (1=allowed, 0=denied)
        foreach (var p in permissions)
        {
            var mask = $"{(p.CanView ? '1' : '0')}" +
                       $"{(p.CanCreate ? '1' : '0')}" +
                       $"{(p.CanEdit ? '1' : '0')}" +
                       $"{(p.CanDelete ? '1' : '0')}" +
                       $"{(p.CanExport ? '1' : '0')}";

            claims.Add(new Claim(ClaimPermission, $"{p.Module}:{mask}"));
        }

        return claims;
    }

    private ClaimsPrincipal? InternalValidate(string token, bool validateLifetime)
    {
        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidIssuer = _jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwt.Audience,
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.Zero, // Match middleware for strict expiry
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha512Signature]
        };

        try
        {
            return handler.ValidateToken(token, parameters, out _);
        }
        catch (SecurityTokenExpiredException) when (!validateLifetime)
        {
            // Expected path for refresh flow
            var newParams = parameters.Clone();
            newParams.ValidateLifetime = false;
            return handler.ValidateToken(token, newParams, out _);
        }
        catch (Exception ex)
        {
            _logJwtValidationFailed(_logger, ex);
            return null;
        }
    }

    private SqlConnection CreateConnection()
        => new(_config.GetConnectionString("DefaultConnection")
               ?? throw new InvalidOperationException("DefaultConnection missing."));
}