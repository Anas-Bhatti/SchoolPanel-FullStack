// ============================================================
// Services/GoogleTokenVerifier.cs
// Verifies Google ID tokens using Google.Apis.Auth
// NuGet: Google.Apis.Auth
// ============================================================

using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SchoolPanel.Auth.Services;

public sealed class GoogleOptions
{
    public const string Section = "Google";

    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
}

public sealed record GoogleUserPayload(
    string Subject,         // Google unique user ID (GoogleId in DB)
    string Email,
    bool EmailVerified,
    string Name,
    string? PictureUrl
);

public interface IGoogleTokenVerifier
{
    /// <summary>
    /// Verify a Google ID token passed from the frontend.
    /// Returns null if verification fails for any reason.
    /// </summary>
    Task<GoogleUserPayload?> VerifyAsync(
        string idToken, CancellationToken ct = default);
}

public sealed class GoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly GoogleOptions _opts;
    private readonly ILogger<GoogleTokenVerifier> _logger;

    public GoogleTokenVerifier(
        IOptions<GoogleOptions> opts,
        ILogger<GoogleTokenVerifier> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task<GoogleUserPayload?> VerifyAsync(
        string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            return null;

        if (string.IsNullOrWhiteSpace(_opts.ClientId))
        {
            _logger.LogError("Google:ClientId is not configured.");
            return null;
        }

        try
        {
            // Google.Apis.Auth performs full cryptographic validation:
            // - Signature from Google's public JWK endpoint
            // - aud must match ClientId
            // - exp must be in the future
            // - iss must be accounts.google.com or accounts.google.com
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_opts.ClientId]
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            _logger.LogDebug(
                "Google token verified. Subject={Sub} Email={Email}",
                payload.Subject, payload.Email);

            return new GoogleUserPayload(
                Subject: payload.Subject,
                Email: payload.Email,
                EmailVerified: payload.EmailVerified,
                Name: payload.Name ?? string.Empty,
                PictureUrl: payload.Picture
            );
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google ID token validation failed.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error verifying Google token.");
            return null;
        }
    }
}