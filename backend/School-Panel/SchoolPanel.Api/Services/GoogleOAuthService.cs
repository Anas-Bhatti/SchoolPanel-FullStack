using System.Text.Json;
using Microsoft.Extensions.Options;
using SchoolPanel.Api.Configuration;

namespace SchoolPanel.Api.Services;

public sealed record GoogleUserInfo(
    string Sub,
    string Email,
    bool EmailVerified,
    string Name,
    string? Picture
);

public interface IGoogleOAuthService
{
    Task<GoogleUserInfo?> VerifyIdTokenAsync(string idToken, CancellationToken ct = default);
}

public sealed class GoogleOAuthService : IGoogleOAuthService
{
    private readonly GoogleOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<GoogleOAuthService> _logger;

    public GoogleOAuthService(
        IOptions<GoogleOptions> options,
        HttpClient http,
        ILogger<GoogleOAuthService> logger)
    {
        _options = options.Value;
        _http = http;
        _logger = logger;
    }

    public async Task<GoogleUserInfo?> VerifyIdTokenAsync(
        string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            return null;

        try
        {
            // Google's tokeninfo endpoint validates the token server-side
            // Production alternative: use Google.Apis.Auth NuGet for offline validation
            var url = $"{_options.TokenInfoUrl}?id_token={Uri.EscapeDataString(idToken)}";
            var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google token validation failed: {Status}",
                    response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Verify aud matches our ClientId
            var aud = root.GetProperty("aud").GetString();
            if (aud != _options.ClientId)
            {
                _logger.LogWarning(
                    "Google token audience mismatch. Expected {Expected}, got {Got}",
                    _options.ClientId, aud);
                return null;
            }

            // Verify token not expired
            if (root.TryGetProperty("exp", out var expEl))
            {
                var exp = long.Parse(expEl.GetString() ?? "0");
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
                {
                    _logger.LogWarning("Google token expired");
                    return null;
                }
            }

            var emailVerified = root.TryGetProperty("email_verified", out var evEl)
                && evEl.GetString() == "true";

            return new GoogleUserInfo(
                Sub: root.GetProperty("sub").GetString() ?? string.Empty,
                Email: root.GetProperty("email").GetString() ?? string.Empty,
                EmailVerified: emailVerified,
                Name: root.TryGetProperty("name", out var nameEl)
                               ? nameEl.GetString() ?? string.Empty
                               : string.Empty,
                Picture: root.TryGetProperty("picture", out var picEl)
                               ? picEl.GetString()
                               : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Google ID token");
            return null;
        }
    }
}