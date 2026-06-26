using System.Security.Cryptography;
using System.Text.Json;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Sidecars;

public sealed class CoverSidecarSyncService(
    IConfigurationService configurationService,
    IImageCacheService imageCacheService,
    IRootFolderRepository rootFolderRepository,
    IFileSystem fileSystem,
    ILogger<CoverSidecarSyncService> logger) : ICoverSidecarSyncService
{
    private const string MarkerFileName = ".listenarr-cover.json";
    private const string MarkerOwner = "listenarr";
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    public async Task<CoverSidecarSyncResult> SyncAsync(
        Audiobook audiobook,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audiobook);

        var settings = await configurationService.GetApplicationSettingsAsync();
        if (!settings.ExportCoverSidecars)
        {
            return new CoverSidecarSyncResult(CoverSidecarSyncStatus.Disabled);
        }

        var sidecarFileName = NormalizeSidecarFileName(settings.CoverSidecarFileName);
        if (sidecarFileName == null)
        {
            return Skip($"Invalid cover sidecar filename '{settings.CoverSidecarFileName}'.");
        }

        if (string.IsNullOrWhiteSpace(audiobook.BasePath))
        {
            return Skip("Audiobook has no base path.");
        }

        if (string.IsNullOrWhiteSpace(audiobook.ImageUrl))
        {
            return Skip("Audiobook has no image URL.");
        }

        var basePath = FileUtils.NormalizeStoredPath(audiobook.BasePath);
        if (!await IsInsideConfiguredLibraryRootAsync(basePath, settings, cancellationToken))
        {
            return Skip("Audiobook base path is outside configured library roots.");
        }

        try
        {
            if (!fileSystem.DirectoryExists(basePath))
            {
                fileSystem.CreateDirectory(basePath);
            }

            if (!TryResolveWritePath(basePath, sidecarFileName, out var sidecarPath, out var sidecarReason))
            {
                return Skip(sidecarReason);
            }

            if (!TryResolveWritePath(basePath, MarkerFileName, out var markerPath, out var markerReason))
            {
                return Skip(markerReason);
            }

            var existingMarker = TryReadMarker(markerPath);
            if (fileSystem.FileExists(sidecarPath) && existingMarker == null)
            {
                return new CoverSidecarSyncResult(
                    CoverSidecarSyncStatus.Skipped,
                    sidecarPath,
                    "Existing sidecar is not managed by Listenarr.");
            }

            if (fileSystem.FileExists(sidecarPath)
                && existingMarker != null
                && !settings.OverwriteManagedCoverSidecars)
            {
                return new CoverSidecarSyncResult(
                    CoverSidecarSyncStatus.Skipped,
                    sidecarPath,
                    "Managed sidecar overwrite is disabled.");
            }

            var imagePath = await imageCacheService.ResolveImageFilePathAsync(
                audiobook.ImageUrl,
                BuildImageIdentifier(audiobook),
                cancellationToken);
            if (string.IsNullOrWhiteSpace(imagePath) || !fileSystem.FileExists(imagePath))
            {
                return Skip("Cover image could not be resolved.");
            }

            var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            if (imageBytes.Length == 0)
            {
                return Skip("Cover image is empty.");
            }

            var checksum = ComputeSha256(imageBytes);
            if (fileSystem.FileExists(sidecarPath)
                && existingMarker != null
                && string.Equals(existingMarker.Sha256, checksum, StringComparison.OrdinalIgnoreCase))
            {
                return new CoverSidecarSyncResult(CoverSidecarSyncStatus.Unchanged, sidecarPath);
            }

            await WriteFileAtomicallyAsync(sidecarPath, imageBytes, cancellationToken);

            var marker = new CoverSidecarMarker(
                MarkerOwner,
                audiobook.ImageUrl,
                checksum,
                DateTimeOffset.UtcNow);
            await WriteFileAtomicallyAsync(
                markerPath,
                JsonSerializer.SerializeToUtf8Bytes(marker),
                cancellationToken);

            logger.LogInformation(
                "Synced managed cover sidecar for audiobook {AudiobookId}: {Path}",
                audiobook.Id,
                LogRedaction.SanitizeFilePath(sidecarPath));
            return new CoverSidecarSyncResult(CoverSidecarSyncStatus.Written, sidecarPath);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            logger.LogWarning(
                exception,
                "Failed to sync cover sidecar for audiobook {AudiobookId}",
                audiobook.Id);
            return new CoverSidecarSyncResult(CoverSidecarSyncStatus.Failed, null, exception.Message);
        }
    }

    private async Task<bool> IsInsideConfiguredLibraryRootAsync(
        string basePath,
        ApplicationSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.OutputPath))
        {
            roots.Add(settings.OutputPath);
        }

        try
        {
            roots.AddRange((await rootFolderRepository.GetAllAsync())
                .Select(root => root.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))!);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            logger.LogDebug(exception, "Failed to load root folders while validating cover sidecar target.");
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(root => FileUtils.IsPathSameOrInside(basePath, root));
    }

    private bool TryResolveWritePath(
        string basePath,
        string fileName,
        out string safePath,
        out string reason)
    {
        safePath = string.Empty;
        var candidate = Path.Join(basePath, fileName);
        if (!fileSystem.TryValidateMutationTarget(candidate, [basePath], out safePath, out reason))
        {
            reason = $"Sidecar path is not safe: {reason}";
            return false;
        }

        return true;
    }

    private CoverSidecarMarker? TryReadMarker(string markerPath)
    {
        try
        {
            if (!fileSystem.FileExists(markerPath))
            {
                return null;
            }

            var marker = JsonSerializer.Deserialize<CoverSidecarMarker>(
                fileSystem.ReadAllText(markerPath));
            return string.Equals(marker?.ManagedBy, MarkerOwner, StringComparison.OrdinalIgnoreCase)
                ? marker
                : null;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            logger.LogDebug(exception, "Failed to read cover sidecar marker {Path}", LogRedaction.SanitizeFilePath(markerPath));
            return null;
        }
    }

    private static async Task WriteFileAtomicallyAsync(
        string destination,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Destination has no parent directory.");
        var tempPath = Path.Join(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, destination, overwrite: true);
    }

    private static string? NormalizeSidecarFileName(string? fileName)
    {
        var normalized = string.IsNullOrWhiteSpace(fileName) ? "cover.jpg" : fileName.Trim();
        if (Path.IsPathRooted(normalized) || normalized != Path.GetFileName(normalized))
        {
            return null;
        }

        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        return AllowedExtensions.Contains(Path.GetExtension(normalized))
            ? normalized
            : null;
    }

    private static string BuildImageIdentifier(Audiobook audiobook)
    {
        if (!string.IsNullOrWhiteSpace(audiobook.Asin))
        {
            return audiobook.Asin;
        }

        return audiobook.Id > 0 ? $"audiobook-{audiobook.Id}" : Guid.NewGuid().ToString("N");
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static CoverSidecarSyncResult Skip(string message)
        => new(CoverSidecarSyncStatus.Skipped, Message: message);

    private sealed record CoverSidecarMarker(
        string ManagedBy,
        string ImageUrl,
        string Sha256,
        DateTimeOffset UpdatedAtUtc);
}
