using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public sealed class ImageCacheService(
    HttpClient httpClient,
    ICachedImageRepository cachedImageRepository,
    ILogger<ImageCacheService> logger) : IImageCacheService
{
    private const int MaxImageSizeBytes = 4 * 1024 * 1024; // 4MB

    private static readonly string[] AllowedHosts =
    [
        "oldschool.runescape.wiki",
        "secure.runescape.com"
    ];

    private static readonly string[] AllowedContentTypePrefixes =
    [
        "image/"
    ];

    public async Task<CachedImage?> GetOrCache(string sourceUrl, ImageProfile profile)
    {
        var existing = await cachedImageRepository.GetBySourceUrl(sourceUrl);
        if (existing is not null)
            return existing;

        if (!IsAllowedUrl(sourceUrl))
        {
            logger.LogWarning("Blocked image fetch from disallowed URL: {Url}", sourceUrl);
            return null;
        }

        try
        {
            using var response = await httpClient.GetAsync(sourceUrl);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch image from {Url}: {StatusCode}", sourceUrl, response.StatusCode);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!AllowedContentTypePrefixes.Any(prefix => contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning("Blocked non-image content type {ContentType} from {Url}", contentType, sourceUrl);
                return null;
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > MaxImageSizeBytes)
            {
                logger.LogWarning("Image too large ({Size} bytes) from {Url}", contentLength, sourceUrl);
                return null;
            }

            var imageData = await response.Content.ReadAsByteArrayAsync();
            if (imageData.Length > MaxImageSizeBytes)
            {
                logger.LogWarning("Image too large ({Size} bytes) from {Url}", imageData.Length, sourceUrl);
                return null;
            }

            var (storedData, storedContentType) = TryEncodeWebp(imageData, contentType, profile, sourceUrl);

            var cached = new CachedImage
            {
                SourceUrl = sourceUrl,
                ImageData = storedData,
                ContentType = storedContentType,
                CachedAt = DateTimeOffset.UtcNow
            };

            return await cachedImageRepository.Save(cached);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch and cache image from {Url}", sourceUrl);
            return null;
        }
    }

    public async Task<CachedImage?> GetOrCacheFromDataUri(string dataUri, ImageProfile profile)
    {
        try
        {
            // Parse "data:{contentType};base64,{base64Data}"
            if (!dataUri.StartsWith("data:", StringComparison.Ordinal))
                return null;

            var commaIndex = dataUri.IndexOf(',');
            if (commaIndex < 0) return null;

            var header = dataUri[5..commaIndex]; // skip "data:"
            if (!header.EndsWith(";base64", StringComparison.Ordinal))
                return null;

            var contentType = header[..^7]; // strip ";base64"
            if (!AllowedContentTypePrefixes.Any(prefix => contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return null;

            var base64Data = dataUri[(commaIndex + 1)..];
            var imageData = Convert.FromBase64String(base64Data);

            if (imageData.Length > MaxImageSizeBytes)
            {
                logger.LogWarning("Data URI image too large ({Size} bytes)", imageData.Length);
                return null;
            }

            // Check if we already have this exact data cached (by source URL = hash of data)
            var sourceKey = $"data-uri:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(imageData))}";
            var existing = await cachedImageRepository.GetBySourceUrl(sourceKey);
            if (existing is not null)
                return existing;

            var (storedData, storedContentType) = TryEncodeWebp(imageData, contentType, profile, sourceKey);

            var cached = new CachedImage
            {
                SourceUrl = sourceKey,
                ImageData = storedData,
                ContentType = storedContentType,
                CachedAt = DateTimeOffset.UtcNow
            };

            return await cachedImageRepository.Save(cached);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse and cache data URI");
            return null;
        }
    }

    private (byte[] Data, string ContentType) TryEncodeWebp(byte[] original, string originalContentType, ImageProfile profile, string source)
    {
        try
        {
            var encoded = WebpEncoder.TryEncode(original, profile);
            return encoded is not null
                ? (encoded, WebpEncoder.ContentType)
                : (original, originalContentType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to re-encode image from {Source} as WebP, storing original", source);
            return (original, originalContentType);
        }
    }

    private static bool IsAllowedUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Scheme == "https"
               && AllowedHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
    }
}
