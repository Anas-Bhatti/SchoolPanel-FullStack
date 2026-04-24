using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using SchoolPanel.Api.Configuration;

namespace SchoolPanel.Api.Services;

public enum UploadFolder
{
    StudentPhotos,
    TeacherDocuments,
    ExamPapers,
    Reports
}

public sealed record UploadResult(
    bool Success,
    string? Url,
    string? BlobName,
    string? Error
);

public interface IFileUploadService
{
    Task<UploadResult> UploadAsync(
        IFormFile file,
        UploadFolder folder,
        string? fileNameOverride = null,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(string blobName, CancellationToken ct = default);
    string GetSasUrl(string blobName, int expiryMinutes = 60);
}

public sealed class FileUploadService : IFileUploadService
{
    private readonly AzureBlobOptions _options;
    private readonly BlobServiceClient _blobService;
    private readonly ILogger<FileUploadService> _logger;

    // Allowed MIME types per folder
    private static readonly Dictionary<UploadFolder, string[]> _allowedTypes = new()
    {
        [UploadFolder.StudentPhotos] = ["image/jpeg", "image/png", "image/webp"],
        [UploadFolder.TeacherDocuments] = ["application/pdf",
                                           "application/msword",
                                           "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                                           "image/jpeg", "image/png"],
        [UploadFolder.ExamPapers] = ["application/pdf",
                                           "application/msword",
                                           "application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
        [UploadFolder.Reports] = ["application/pdf",
                                           "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]
    };

    // Max sizes per folder (bytes)
    private static readonly Dictionary<UploadFolder, long> _maxSizes = new()
    {
        [UploadFolder.StudentPhotos] = 2 * 1024 * 1024,  // 2 MB
        [UploadFolder.TeacherDocuments] = 10 * 1024 * 1024,  // 10 MB
        [UploadFolder.ExamPapers] = 20 * 1024 * 1024,  // 20 MB
        [UploadFolder.Reports] = 10 * 1024 * 1024   // 10 MB
    };

    private static readonly Dictionary<UploadFolder, string> _folderPaths = new()
    {
        [UploadFolder.StudentPhotos] = "students/photos",
        [UploadFolder.TeacherDocuments] = "teachers/documents",
        [UploadFolder.ExamPapers] = "exams/papers",
        [UploadFolder.Reports] = "reports"
    };

    public FileUploadService(
        IOptions<AzureBlobOptions> options,
        ILogger<FileUploadService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _blobService = new BlobServiceClient(_options.ConnectionString);
    }

    public async Task<UploadResult> UploadAsync(
        IFormFile file,
        UploadFolder folder,
        string? fileNameOverride = null,
        CancellationToken ct = default)
    {
        // ── Validate content type ─────────────────────────────────────────────
        if (!_allowedTypes[folder].Contains(file.ContentType))
        {
            var allowed = string.Join(", ", _allowedTypes[folder]);
            return new UploadResult(false, null, null,
                $"File type '{file.ContentType}' not allowed. Allowed: {allowed}");
        }

        // ── Validate size ─────────────────────────────────────────────────────
        if (file.Length > _maxSizes[folder])
        {
            var maxMb = _maxSizes[folder] / 1024 / 1024;
            return new UploadResult(false, null, null,
                $"File exceeds maximum size of {maxMb} MB.");
        }

        // ── Validate magic bytes (prevent MIME spoofing) ──────────────────────
        if (!await IsValidMagicBytesAsync(file, folder))
            return new UploadResult(false, null, null,
                "File content does not match its declared type.");

        try
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var blobName = fileNameOverride != null
                ? $"{_folderPaths[folder]}/{fileNameOverride}{ext}"
                : $"{_folderPaths[folder]}/{Guid.NewGuid()}{ext}";

            var container = _blobService.GetBlobContainerClient(_options.ContainerName);
            await container.CreateIfNotExistsAsync(
                PublicAccessType.None, cancellationToken: ct);

            var blob = container.GetBlobClient(blobName);

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = file.ContentType,
                    ContentDisposition = $"inline; filename=\"{Path.GetFileName(file.FileName)}\""
                }
            };

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, uploadOptions, ct);

            _logger.LogInformation(
                "File uploaded. BlobName={BlobName} Size={Size} Type={Type}",
                blobName, file.Length, file.ContentType);

            return new UploadResult(true, blob.Uri.ToString(), blobName, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Blob upload failed");
            return new UploadResult(false, null, null, "Upload failed. Please try again.");
        }
    }

    public async Task<bool> DeleteAsync(string blobName, CancellationToken ct = default)
    {
        try
        {
            var container = _blobService.GetBlobContainerClient(_options.ContainerName);
            var blob = container.GetBlobClient(blobName);
            var response = await blob.DeleteIfExistsAsync(cancellationToken: ct);
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Blob delete failed. BlobName={BlobName}", blobName);
            return false;
        }
    }

    public string GetSasUrl(string blobName, int expiryMinutes = 60)
    {
        // Generate time-limited SAS URL for secure file access
        var container = _blobService.GetBlobContainerClient(_options.ContainerName);
        var blob = container.GetBlobClient(blobName);

        if (blob.CanGenerateSasUri)
        {
            var sasBuilder = new Azure.Storage.Sas.BlobSasBuilder
            {
                BlobContainerName = _options.ContainerName,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
            };
            sasBuilder.SetPermissions(Azure.Storage.Sas.BlobSasPermissions.Read);
            return blob.GenerateSasUri(sasBuilder).ToString();
        }

        return blob.Uri.ToString();
    }

    // ── Magic byte validation — check actual file header ─────────────────────
    private static async Task<bool> IsValidMagicBytesAsync(
        IFormFile file, UploadFolder folder)
    {
        // Only validate image types — documents are harder to spoof meaningfully
        if (!_allowedTypes[folder].Any(t => t.StartsWith("image/")))
            return true;

        if (!file.ContentType.StartsWith("image/"))
            return true;

        var buffer = new byte[4];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer.AsMemory(0, 4));
        if (read < 4) return false;

        // JPEG: FF D8 FF
        if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
            return file.ContentType is "image/jpeg";

        // PNG: 89 50 4E 47
        if (buffer[0] == 0x89 && buffer[1] == 0x50 &&
            buffer[2] == 0x4E && buffer[3] == 0x47)
            return file.ContentType is "image/png";

        // WebP: checked by RIFF header (first 4 = 52 49 46 46)
        if (buffer[0] == 0x52 && buffer[1] == 0x49 &&
            buffer[2] == 0x46 && buffer[3] == 0x46)
            return file.ContentType is "image/webp";

        return false;
    }
}