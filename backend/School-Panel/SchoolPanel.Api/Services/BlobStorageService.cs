// ============================================================
// Services/BlobStorageService.cs
// Azure Blob Storage abstraction for student photos,
// documents, and generated PDF receipts.
// NuGet: Azure.Storage.Blobs
// ============================================================

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SchoolPanel.Controllers.Services;

// ─── Options ──────────────────────────────────────────────────

public sealed class BlobOptions
{
    public const string Section = "AzureBlob";

    public string ConnectionString { get; init; } = string.Empty;
    public string ContainerName { get; init; } = "schoolpanel";
    public long MaxPhotoBytes { get; init; } = 2 * 1024 * 1024;   // 2 MB
    public long MaxDocBytes { get; init; } = 10 * 1024 * 1024;  // 10 MB
    public int SasExpiryMinutes { get; init; } = 60;
}

// ─── Upload result ────────────────────────────────────────────

public sealed record BlobUploadResult(
    bool Success,
    string? Url,
    string? BlobName,
    string? Error
);

// ─── Allowed upload categories ────────────────────────────────

public enum BlobFolder
{
    StudentPhotos,
    TeacherDocuments,
    ExamPapers,
    Receipts
}

// ─── Interface ────────────────────────────────────────────────

public interface IBlobStorageService
{
    Task<BlobUploadResult> UploadPhotoAsync(
        IFormFile file, BlobFolder folder,
        string fileNameStem, CancellationToken ct = default);

    Task<BlobUploadResult> UploadBytesAsync(
        byte[] data, string contentType, BlobFolder folder,
        string fileName, CancellationToken ct = default);

    Task<bool> DeleteAsync(
        string blobName, CancellationToken ct = default);

    /// <summary>Return a time-limited SAS URL (read-only).</summary>
    string GetSasUrl(string blobName, int expiryMinutes = 60);

    /// <summary>Stream a blob directly — for PDF receipts.</summary>
    Task<Stream?> OpenReadAsync(
        string blobName, CancellationToken ct = default);
}

// ─── Implementation ───────────────────────────────────────────

public sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobOptions _opts;
    private readonly BlobServiceClient _client;
    private readonly ILogger<BlobStorageService> _log;

    private static readonly Dictionary<BlobFolder, string> FolderPaths = new()
    {
        [BlobFolder.StudentPhotos] = "students/photos",
        [BlobFolder.TeacherDocuments] = "teachers/docs",
        [BlobFolder.ExamPapers] = "exams/papers",
        [BlobFolder.Receipts] = "fees/receipts"
    };

    private static readonly Dictionary<BlobFolder, string[]> AllowedMimes = new()
    {
        [BlobFolder.StudentPhotos] = ["image/jpeg", "image/png", "image/webp"],
        [BlobFolder.TeacherDocuments] = ["application/pdf", "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
        [BlobFolder.ExamPapers] = ["application/pdf"],
        [BlobFolder.Receipts] = ["application/pdf"]
    };

    public BlobStorageService(
        IOptions<BlobOptions> opts,
        ILogger<BlobStorageService> log)
    {
        _opts = opts.Value;
        _log = log;
        _client = new BlobServiceClient(_opts.ConnectionString);
    }

    // ── Upload from IFormFile ─────────────────────────────────
    public async Task<BlobUploadResult> UploadPhotoAsync(
        IFormFile file, BlobFolder folder,
        string fileNameStem, CancellationToken ct = default)
    {
        // ── Validate MIME ─────────────────────────────────────
        if (!AllowedMimes[folder].Contains(file.ContentType))
            return new BlobUploadResult(false, null, null,
                $"File type '{file.ContentType}' is not allowed.");

        // ── Validate size ─────────────────────────────────────
        var maxSize = folder == BlobFolder.StudentPhotos
            ? _opts.MaxPhotoBytes
            : _opts.MaxDocBytes;

        if (file.Length > maxSize)
            return new BlobUploadResult(false, null, null,
                $"File exceeds the {maxSize / 1024 / 1024} MB limit.");

        // ── Validate magic bytes (prevent MIME spoofing) ──────
        if (!await ValidateMagicAsync(file))
            return new BlobUploadResult(false, null, null,
                "File contents do not match the declared type.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var blobName = $"{FolderPaths[folder]}/{fileNameStem}{ext}";

        await using var stream = file.OpenReadStream();
        return await UploadStreamAsync(stream, file.ContentType, blobName, ct);
    }

    // ── Upload raw bytes (generated PDFs) ─────────────────────
    public async Task<BlobUploadResult> UploadBytesAsync(
        byte[] data, string contentType, BlobFolder folder,
        string fileName, CancellationToken ct = default)
    {
        var blobName = $"{FolderPaths[folder]}/{fileName}";
        using var ms = new MemoryStream(data);
        return await UploadStreamAsync(ms, contentType, blobName, ct);
    }

    // ── Delete ────────────────────────────────────────────────
    public async Task<bool> DeleteAsync(
        string blobName, CancellationToken ct = default)
    {
        try
        {
            var container = _client.GetBlobContainerClient(_opts.ContainerName);
            var result = await container
                .GetBlobClient(blobName)
                .DeleteIfExistsAsync(cancellationToken: ct);
            return result.Value;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Blob delete failed. BlobName={N}", blobName);
            return false;
        }
    }

    // ── SAS URL ───────────────────────────────────────────────
    public string GetSasUrl(string blobName, int expiryMinutes = 60)
    {
        var container = _client.GetBlobContainerClient(_opts.ContainerName);
        var blob = container.GetBlobClient(blobName);

        if (!blob.CanGenerateSasUri)
            return blob.Uri.ToString();

        var sas = new BlobSasBuilder
        {
            BlobContainerName = _opts.ContainerName,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
        };
        sas.SetPermissions(BlobSasPermissions.Read);
        return blob.GenerateSasUri(sas).ToString();
    }

    // ── Stream read (PDF download) ────────────────────────────
    public async Task<Stream?> OpenReadAsync(
        string blobName, CancellationToken ct = default)
    {
        try
        {
            var blob = _client
                .GetBlobContainerClient(_opts.ContainerName)
                .GetBlobClient(blobName);

            var response = await blob.OpenReadAsync(cancellationToken: ct);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Blob open-read failed. BlobName={N}", blobName);
            return null;
        }
    }

    // ─── Private helpers ──────────────────────────────────────

    private async Task<BlobUploadResult> UploadStreamAsync(
        Stream stream, string contentType,
        string blobName, CancellationToken ct)
    {
        try
        {
            var container = _client.GetBlobContainerClient(_opts.ContainerName);
            await container.CreateIfNotExistsAsync(
                PublicAccessType.None, cancellationToken: ct);

            var blob = container.GetBlobClient(blobName);
            var headers = new BlobHttpHeaders { ContentType = contentType };

            await blob.UploadAsync(stream,
                new BlobUploadOptions { HttpHeaders = headers },
                cancellationToken: ct);

            var url = blob.Uri.ToString();
            _log.LogInformation("Blob uploaded. Name={N}", blobName);
            return new BlobUploadResult(true, url, blobName, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Blob upload failed. BlobName={N}", blobName);
            return new BlobUploadResult(false, null, null,
                "Upload failed. Please try again.");
        }
    }

    private static async Task<bool> ValidateMagicAsync(IFormFile file)
    {
        if (!file.ContentType.StartsWith("image/")) return true;

        var buf = new byte[4];
        await using var s = file.OpenReadStream();
        if (await s.ReadAsync(buf.AsMemory(0, 4)) < 4) return false;

        return file.ContentType switch
        {
            "image/jpeg" => buf[0] == 0xFF && buf[1] == 0xD8 && buf[2] == 0xFF,
            "image/png" => buf[0] == 0x89 && buf[1] == 0x50 &&
                            buf[2] == 0x4E && buf[3] == 0x47,
            "image/webp" => buf[0] == 0x52 && buf[1] == 0x49 &&
                            buf[2] == 0x46 && buf[3] == 0x46,
            _ => false
        };
    }
}