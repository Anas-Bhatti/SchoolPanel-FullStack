namespace SchoolPanel.Api.Configuration;

// ─── JWT ─────────────────────────────────────────────────────────────────────
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; init; } = "schoolpanel";
    public string Audience { get; init; } = "schoolpanel-api";
    public string SecretKey { get; init; } = string.Empty;
    public string Algorithm { get; init; } = "HS512";
    public int AccessTokenExpiryMinutes { get; init; } = 15;
    public int RefreshTokenExpiryDays { get; init; } = 7;
}

// ─── Google OAuth ─────────────────────────────────────────────────────────────
public sealed class GoogleOptions
{
    public const string Section = "Google";

    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string TokenInfoUrl { get; init; } = "https://oauth2.googleapis.com/tokeninfo";
}

// ─── TOTP / Two-Factor ────────────────────────────────────────────────────────
public sealed class TwoFactorOptions
{
    public const string Section = "TwoFactor";

    public string Issuer { get; init; } = "SchoolPanel";
    public int Digits { get; init; } = 6;
    public int PeriodSeconds { get; init; } = 30;
    public string Algorithm { get; init; } = "SHA1";
}

// ─── Security ─────────────────────────────────────────────────────────────────
public sealed class SecurityOptions
{
    public const string Section = "Security";

    public int MaxLoginAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 30;
    public int BcryptWorkFactor { get; init; } = 12;
    public bool RequireHttps { get; init; } = true;
    public int HstsMaxAgeDays { get; init; } = 365;
}

// ─── Rate Limit ───────────────────────────────────────────────────────────────
public sealed class RateLimitWindowOptions
{
    public int PermitLimit { get; init; } = 60;
    public int WindowSeconds { get; init; } = 60;
}

public sealed class RateLimitOptions
{
    public const string Section = "RateLimit";

    public RateLimitWindowOptions PerIp { get; init; } = new();
    public RateLimitWindowOptions PerUser { get; init; } = new();
    public RateLimitWindowOptions AuthEndpoint { get; init; } = new();
}

// ─── CORS ─────────────────────────────────────────────────────────────────────
public sealed class CorsOptions
{
    public const string Section = "Cors";
    public const string PolicyName = "SchoolPanelCors";

    public string[] AllowedOrigins { get; init; } = [];
}

// ─── Azure Blob ───────────────────────────────────────────────────────────────
public sealed class AzureBlobOptions
{
    public const string Section = "AzureBlob";

    public string ConnectionString { get; init; } = string.Empty;
    public string ContainerName { get; init; } = "schoolpanel-uploads";
    public long MaxFileSizeBytes { get; init; } = 10_485_760; // 10 MB
}