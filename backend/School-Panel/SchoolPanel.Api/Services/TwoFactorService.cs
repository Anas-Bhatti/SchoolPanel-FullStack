// ============================================================
// Services/TwoFactorService.cs
// TOTP 2FA using Otp.NET (OtpSharp)
// Handles: QR URI generation, code verification, enable/disable,
//          recovery codes, encrypted secret storage
// NuGet: Otp.NET, BCrypt.Net-Next
// ============================================================

using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtpNet;

namespace SchoolPanel.Auth.Services;

// ─── Options ──────────────────────────────────────────────────

public sealed class TotpOptions
{
    public const string Section = "TwoFactor";

    public string Issuer { get; init; } = "SchoolPanel";
    public int Digits { get; init; } = 6;
    public int PeriodSeconds { get; init; } = 30;
    /// <summary>Number of one-time recovery codes generated at setup</summary>
    public int RecoveryCodeCount { get; init; } = 8;
}

// ─── Interface ────────────────────────────────────────────────

public interface ITwoFactorService
{
    /// <summary>Generate a new Base32 secret + QR URI + recovery codes for setup.</summary>
    Task<(string SecretKey, string QrCodeUri, string ManualEntryKey,
          IReadOnlyList<string> RecoveryCodes)>
        GenerateSetupAsync(Guid userId, string email, CancellationToken ct = default);

    /// <summary>
    /// Verify a TOTP code against the user's stored (encrypted) secret.
    /// Applies ±1 step clock-skew tolerance.
    /// </summary>
    Task<bool> VerifyCodeAsync(
        Guid userId, string code, CancellationToken ct = default);

    /// <summary>
    /// Verify a recovery code (one-time use, then mark consumed).
    /// </summary>
    Task<bool> VerifyRecoveryCodeAsync(
        Guid userId, string code, CancellationToken ct = default);

    /// <summary>
    /// Enable 2FA after first successful code verification.
    /// Stores encrypted secret permanently.
    /// </summary>
    Task EnableAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Disable 2FA and wipe the stored secret.
    /// </summary>
    Task DisableAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Load the TOTP secret for a user (decrypted, for verify operations).
    /// Returns null if 2FA is not set up.
    /// </summary>
    Task<string?> GetDecryptedSecretAsync(Guid userId, CancellationToken ct = default);
}

// ─── Implementation ───────────────────────────────────────────

public sealed class TwoFactorService : ITwoFactorService
{
    private readonly TotpOptions _opts;
    private readonly IConfiguration _config;
    private readonly ILogger<TwoFactorService> _logger;

    // Derived AES-256 key from the JWT secret — no extra secret to manage
    private readonly Lazy<byte[]> _encryptionKey;

    public TwoFactorService(
        IOptions<TotpOptions> opts,
        IConfiguration config,
        ILogger<TwoFactorService> logger)
    {
        _opts = opts.Value;
        _config = config;
        _logger = logger;

        _encryptionKey = new Lazy<byte[]>(() =>
        {
            var jwtSecret = config["Jwt:SecretKey"]
                ?? throw new InvalidOperationException("Jwt:SecretKey not configured.");

            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(jwtSecret));
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Generate Setup Payload
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<(string SecretKey, string QrCodeUri, string ManualEntryKey,
                        IReadOnlyList<string> RecoveryCodes)>
        GenerateSetupAsync(Guid userId, string email, CancellationToken ct = default)
    {
        // 20-byte (160-bit) secret — RFC 4226 minimum recommended
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secretBase32 = Base32Encoding.ToString(secretBytes);

        // otpauth:// URI — compatible with Google Authenticator, Authy, 1Password
        var label = Uri.EscapeDataString($"{_opts.Issuer}:{email}");
        var issuer = Uri.EscapeDataString(_opts.Issuer);
        var qrUri = $"otpauth://totp/{label}" +
                      $"?secret={secretBase32}" +
                      $"&issuer={issuer}" +
                      $"&digits={_opts.Digits}" +
                      $"&period={_opts.PeriodSeconds}" +
                      $"&algorithm=SHA1";

        // Manual entry key: groups of 4 chars separated by spaces
        var manual = string.Join(" ", Enumerable
            .Range(0, (int)Math.Ceiling(secretBase32.Length / 4.0))
            .Select(i => secretBase32.Substring(
                i * 4,
                Math.Min(4, secretBase32.Length - i * 4))));

        // Recovery codes: 8 × 16-char random alphanumeric (hyphen in middle for readability)
        var recoveryCodes = GenerateRecoveryCodes(_opts.RecoveryCodeCount);

        // Persist: encrypted secret (not yet enabled), hashed recovery codes
        await PersistSetupAsync(userId, secretBase32, recoveryCodes, ct);

        _logger.LogInformation(
            "2FA setup initiated for UserId={UserId}", userId);

        return (secretBase32, qrUri, manual.Trim(), recoveryCodes);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Verify TOTP Code
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> VerifyCodeAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        code = code.Trim().Replace(" ", "");

        if (code.Length != _opts.Digits || !code.All(char.IsDigit))
            return false;

        var secret = await GetDecryptedSecretAsync(userId, ct);
        if (secret is null)
        {
            _logger.LogWarning("2FA verify: no secret found for UserId={UserId}", userId);
            return false;
        }

        var secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes,
            step: _opts.PeriodSeconds,
            totpSize: _opts.Digits);

        // VerificationWindow of 1 = accepts code from previous + current + next window
        // This handles up to 30s clock skew between server and authenticator
        var verified = totp.VerifyTotp(
            code,
            out long _,
            new VerificationWindow(previous: 1, future: 1));

        if (!verified)
            _logger.LogWarning(
                "Invalid 2FA code for UserId={UserId}", userId);

        return verified;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Verify Recovery Code
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> VerifyRecoveryCodeAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        code = code.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
        if (string.IsNullOrEmpty(code)) return false;

        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        // Load all unused recovery code hashes for this user
        var rows = await conn.QueryAsync<dynamic>(
            """
            SELECT RecoveryCodeId, CodeHash
            FROM   dbo.TwoFactorRecoveryCodes
            WHERE  UserId = @UserId AND IsUsed = 0
            """,
            new { UserId = userId });

        foreach (var row in rows)
        {
            // Constant-time BCrypt verify — prevents timing attacks
            if (BCrypt.Net.BCrypt.Verify(code, (string)row.CodeHash))
            {
                // Mark this specific code as used (one-time only)
                await conn.ExecuteAsync(
                    """
                    UPDATE dbo.TwoFactorRecoveryCodes
                    SET    IsUsed = 1, UsedAt = SYSUTCDATETIME()
                    WHERE  RecoveryCodeId = @Id
                    """,
                    new { Id = (int)row.RecoveryCodeId });

                _logger.LogWarning(
                    "Recovery code used for UserId={UserId}. Code exhausted.", userId);
                return true;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Enable 2FA
    // ─────────────────────────────────────────────────────────────────────────
    public async Task EnableAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(
            """
            UPDATE dbo.Users
            SET    TwoFactorEnabled = 1,
                   UpdatedAt        = SYSUTCDATETIME()
            WHERE  UserId = @UserId
            """,
            new { UserId = userId });

        _logger.LogInformation("2FA enabled for UserId={UserId}", userId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Disable 2FA
    // ─────────────────────────────────────────────────────────────────────────
    public async Task DisableAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        using var tx = await conn.BeginTransactionAsync(ct);

        // Wipe secret from Users table
        await conn.ExecuteAsync(
            """
            UPDATE dbo.Users
            SET    TwoFactorEnabled = 0,
                   TwoFactorSecret  = NULL,
                   UpdatedAt        = SYSUTCDATETIME()
            WHERE  UserId = @UserId
            """,
            new { UserId = userId },
            transaction: tx);

        // Delete all recovery codes
        await conn.ExecuteAsync(
            "DELETE FROM dbo.TwoFactorRecoveryCodes WHERE UserId = @UserId",
            new { UserId = userId },
            transaction: tx);

        await tx.CommitAsync(ct);

        _logger.LogInformation("2FA disabled for UserId={UserId}", userId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Get Decrypted Secret
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<string?> GetDecryptedSecretAsync(
        Guid userId, CancellationToken ct = default)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var encrypted = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT TwoFactorSecret FROM dbo.Users WHERE UserId = @UserId AND IsDeleted = 0",
            new { UserId = userId });

        if (string.IsNullOrEmpty(encrypted))
            return null;

        try
        {
            return Decrypt(encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to decrypt 2FA secret for UserId={UserId}", userId);
            return null;
        }
    }

    // ─── Private helpers ──────────────────────────────────────

    private async Task PersistSetupAsync(
        Guid userId, string secretBase32,
        IReadOnlyList<string> recoveryCodes,
        CancellationToken ct)
    {
        using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        using var tx = await conn.BeginTransactionAsync(ct);

        // Encrypt secret before storage
        var encrypted = Encrypt(secretBase32);

        // Store encrypted secret (TwoFactorEnabled stays FALSE until verify-setup)
        await conn.ExecuteAsync(
            """
            UPDATE dbo.Users
            SET    TwoFactorSecret = @Secret,
                   UpdatedAt       = SYSUTCDATETIME()
            WHERE  UserId = @UserId
            """,
            new { UserId = userId, Secret = encrypted },
            transaction: tx);

        // Wipe any previous recovery codes
        await conn.ExecuteAsync(
            "DELETE FROM dbo.TwoFactorRecoveryCodes WHERE UserId = @UserId",
            new { UserId = userId },
            transaction: tx);

        // Insert new hashed recovery codes
        foreach (var code in recoveryCodes)
        {
            // BCrypt work factor 10 — recovery codes don't need factor 12;
            // they are already high-entropy and used only once
            var hash = BCrypt.Net.BCrypt.HashPassword(
                code.Replace("-", ""),
                workFactor: 10);

            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.TwoFactorRecoveryCodes (UserId, CodeHash, IsUsed)
                VALUES (@UserId, @CodeHash, 0)
                """,
                new { UserId = userId, CodeHash = hash },
                transaction: tx);
        }

        await tx.CommitAsync(ct);
    }

    private IReadOnlyList<string> GenerateRecoveryCodes(int count)
    {
        var codes = new List<string>(count);
        var chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // No 0/O/I/1 to avoid confusion

        for (var i = 0; i < count; i++)
        {
            var bytes = new byte[8];
            RandomNumberGenerator.Fill(bytes);

            var code = new char[16];
            for (var j = 0; j < 16; j++)
                code[j] = chars[bytes[j % 8] % chars.Length];

            // Format as XXXXXXXX-XXXXXXXX for readability
            codes.Add($"{new string(code, 0, 8)}-{new string(code, 8, 8)}");
        }

        return codes.AsReadOnly();
    }

    // ─── AES-256-CBC encryption for TOTP secret ───────────────────────────────

    private string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey.Value;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        using var enc = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Output: Base64(IV || CipherText)
        var result = new byte[16 + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, 16);
        Buffer.BlockCopy(cipherBytes, 0, result, 16, cipherBytes.Length);
        return Convert.ToBase64String(result);
    }

    private string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        if (data.Length < 17) throw new ArgumentException("Invalid ciphertext.");

        var iv = new byte[16];
        var cipher = new byte[data.Length - 16];
        Buffer.BlockCopy(data, 0, iv, 0, 16);
        Buffer.BlockCopy(data, 16, cipher, 0, cipher.Length);

        using var aes = Aes.Create();
        aes.Key = _encryptionKey.Value;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plain);
    }

    private SqlConnection CreateConnection()
        => new(_config.GetConnectionString("DefaultConnection")
               ?? throw new InvalidOperationException("DefaultConnection missing."));
}