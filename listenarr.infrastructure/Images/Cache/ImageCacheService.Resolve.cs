/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Images.Cache
{
    public partial class ImageCacheService
    {
        public async Task<string?> ResolveImageFilePathAsync(
            string imageUrl,
            string identifier,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return null;
            }

            var source = imageUrl.Trim();
            if (TryExtractApiImageRequest(source, out var apiIdentifier, out var fallbackUrl))
            {
                var cachedPath = await GetCachedImagePathAsync(apiIdentifier);
                var resolved = ResolveCachedImagePath(cachedPath);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }

                if (!string.IsNullOrWhiteSpace(fallbackUrl))
                {
                    source = fallbackUrl;
                }
            }

            if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var cacheIdentifier = string.IsNullOrWhiteSpace(identifier)
                    ? "sidecar-" + ComputeShortHash(source)
                    : $"{identifier}-{ComputeShortHash(source)}";
                var cached = await DownloadAndCacheImageAsync(source, cacheIdentifier);
                cancellationToken.ThrowIfCancellationRequested();
                return ResolveCachedImagePath(cached);
            }

            return ResolveCachedImagePath(source);
        }

        private string? ResolveCachedImagePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var candidates = new List<string>();
            var normalized = path.Trim().TrimStart('/', '\\');

            try
            {
                if (Path.IsPathRooted(path))
                {
                    candidates.Add(Path.GetFullPath(path));
                }

                if (!Path.IsPathRooted(path) || LooksLikeAppRelativeCachePath(normalized))
                {
                    candidates.Add(Path.GetFullPath(Path.Join(_contentRootPath, normalized)));
                    candidates.Add(Path.GetFullPath(Path.Join(_contentRootPath, "config", normalized)));
                }
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                _logger.LogDebug(exception, "Failed to normalize cached image path {Path}", LogRedaction.SanitizeText(path));
                return null;
            }

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!FileSystemSafety.TryValidateMutationTarget(candidate, [_contentRootPath], out var safePath, out var reason))
                {
                    _logger.LogDebug(
                        "Rejected cached image path {Path}: {Reason}",
                        LogRedaction.SanitizeText(candidate),
                        LogRedaction.SanitizeText(reason));
                    continue;
                }

                if (File.Exists(safePath))
                {
                    return safePath;
                }
            }

            return null;
        }

        private static bool LooksLikeAppRelativeCachePath(string path)
            => path.StartsWith("config/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("config\\", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("cache/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("cache\\", StringComparison.OrdinalIgnoreCase);

        private static bool TryExtractApiImageRequest(
            string value,
            out string identifier,
            out string? fallbackUrl)
        {
            identifier = string.Empty;
            fallbackUrl = null;

            var path = value;
            var query = string.Empty;
            if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
            {
                path = absoluteUri.AbsolutePath;
                query = absoluteUri.Query;
            }
            else
            {
                var queryIndex = value.IndexOf('?', StringComparison.Ordinal);
                if (queryIndex >= 0)
                {
                    path = value[..queryIndex];
                    query = value[(queryIndex + 1)..];
                }
            }

            var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var imageIndex = Array.FindIndex(segments, segment =>
                string.Equals(segment, "images", StringComparison.OrdinalIgnoreCase));
            if (imageIndex < 0 || imageIndex + 1 >= segments.Length)
            {
                return false;
            }

            identifier = Uri.UnescapeDataString(segments[imageIndex + 1]);
            fallbackUrl = ExtractQueryValue(query, "url");
            return !string.IsNullOrWhiteSpace(identifier);
        }

        private static string? ExtractQueryValue(string query, string key)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=', StringComparison.Ordinal);
                var name = separator >= 0 ? part[..separator] : part;
                if (!string.Equals(Uri.UnescapeDataString(name), key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = separator >= 0 ? part[(separator + 1)..] : string.Empty;
                return string.IsNullOrWhiteSpace(value) ? null : Uri.UnescapeDataString(value);
            }

            return null;
        }

        private static string ComputeShortHash(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant()[..16];
        }
    }
}
