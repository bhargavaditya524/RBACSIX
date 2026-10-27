using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace RBACSIX.Services;

public sealed class PrivateFileStorageOptions
{
    public string? RootPath { get; set; }
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;
}

public sealed record StoredUpload(string StoredFileName, string OriginalFileName, string ContentType);

public sealed class InvalidUploadException(string message) : Exception(message);

public interface IPrivateFileStorage
{
    Task<StoredUpload> SavePdfMaterialAsync(IFormFile file, CancellationToken cancellationToken);
    Task<StoredUpload> SaveSubmissionAsync(IFormFile file, CancellationToken cancellationToken);
    Task<StoredUpload> SaveQuizAnswerAsync(IFormFile file, CancellationToken cancellationToken);
    FileStream OpenRead(string storedFileName);
    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default);
}

public sealed class PrivateFileStorage : IPrivateFileStorage
{
    private static readonly IReadOnlyDictionary<string, string> SubmissionContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp"
        };

    private static readonly IReadOnlyDictionary<string, string> QuizAnswerContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp"
        };

    private readonly string _rootPath;
    private readonly long _maxFileBytes;
    private readonly ILogger<PrivateFileStorage> _logger;

    public PrivateFileStorage(
        IWebHostEnvironment environment,
        IOptions<PrivateFileStorageOptions> options,
        ILogger<PrivateFileStorage> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "uploads")
            : Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(environment.ContentRootPath, configuredPath));
        if (!string.IsNullOrWhiteSpace(environment.WebRootPath) &&
            IsUnderDirectory(_rootPath, Path.GetFullPath(environment.WebRootPath)))
            throw new InvalidOperationException("Private file storage cannot be configured beneath the public web root.");
        _maxFileBytes = options.Value.MaxFileBytes > 0
            ? options.Value.MaxFileBytes
            : throw new InvalidOperationException("Files:MaxFileBytes must be greater than zero.");
        _logger = logger;
    }

    public Task<StoredUpload> SavePdfMaterialAsync(IFormFile file, CancellationToken cancellationToken)
    {
        return SaveAsync(file, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf"
        }, cancellationToken);
    }

    public Task<StoredUpload> SaveSubmissionAsync(IFormFile file, CancellationToken cancellationToken)
    {
        return SaveAsync(file, SubmissionContentTypes, cancellationToken);
    }

    public Task<StoredUpload> SaveQuizAnswerAsync(IFormFile file, CancellationToken cancellationToken)
    {
        return SaveAsync(file, QuizAnswerContentTypes, cancellationToken);
    }

    public FileStream OpenRead(string storedFileName)
    {
        var path = GetSafePath(storedFileName);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
    }

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetSafePath(storedFileName);
        try
        {
            File.Delete(path);
            return Task.CompletedTask;
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Could not delete private upload {StoredFileName}.", storedFileName);
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(exception, "Could not delete private upload {StoredFileName}.", storedFileName);
            throw;
        }
    }

    private async Task<StoredUpload> SaveAsync(
        IFormFile file,
        IReadOnlyDictionary<string, string> allowedContentTypes,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
            throw new InvalidUploadException("Select a non-empty file.");
        if (file.Length > _maxFileBytes)
            throw new InvalidUploadException($"Files must be {_maxFileBytes / (1024 * 1024)} MB or smaller.");

        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        if (!allowedContentTypes.TryGetValue(extension, out var contentType))
            throw new InvalidUploadException("This file type is not allowed.");

        var signature = new byte[12];
        await using (var input = file.OpenReadStream())
        {
            var read = 0;
            while (read < signature.Length)
            {
                var bytesRead = await input.ReadAsync(signature.AsMemory(read), cancellationToken);
                if (bytesRead == 0)
                    break;
                read += bytesRead;
            }
            if (!HasValidSignature(extension, signature.AsSpan(0, read)))
                throw new InvalidUploadException("The file contents do not match the selected file type.");
        }

        var originalFileName = Path.GetFileName(file.FileName);
        if (originalFileName.Length > 255)
            originalFileName = originalFileName[^255..];
        var storedFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var destinationPath = GetSafePath(storedFileName);
        Directory.CreateDirectory(_rootPath);
        var temporaryPath = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await file.CopyToAsync(output, cancellationToken);
            }

            File.Move(temporaryPath, destinationPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(cleanupException, "Could not remove incomplete upload {TemporaryFileName}.", Path.GetFileName(temporaryPath));
            }
            _logger.LogError(exception, "Could not save private upload {StoredFileName}.", storedFileName);
            throw;
        }

        return new StoredUpload(storedFileName, originalFileName, contentType);
    }

    private string GetSafePath(string storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName) ||
            !string.Equals(Path.GetFileName(storedFileName), storedFileName, StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileNameWithoutExtension(storedFileName), "N", out _) ||
            !SubmissionContentTypes.ContainsKey(Path.GetExtension(storedFileName)))
            throw new InvalidOperationException("The stored upload key is invalid.");

        return Path.Combine(_rootPath, storedFileName);
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relativePath) &&
               (relativePath == "." ||
                relativePath != ".." &&
                !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static bool HasValidSignature(string extension, ReadOnlySpan<byte> bytes)
    {
        return extension.ToLowerInvariant() switch
        {
            ".pdf" => bytes.StartsWith("%PDF-"u8),
            ".docx" or ".xlsx" or ".pptx" => bytes.StartsWith("PK\x03\x04"u8),
            ".png" => bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".jpg" or ".jpeg" => bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            ".gif" => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8),
            ".webp" => bytes.Length >= 12 &&
                       bytes[..4].SequenceEqual("RIFF"u8) &&
                       bytes[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };
    }
}
