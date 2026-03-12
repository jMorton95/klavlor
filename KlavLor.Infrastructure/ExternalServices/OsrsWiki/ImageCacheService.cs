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

    public async Task<CachedImage?> GetOrCache(string sourceUrl)
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

            var cached = new CachedImage
            {
                SourceUrl = sourceUrl,
                ImageData = imageData,
                ContentType = contentType,
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

    private static bool IsAllowedUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && uri.Scheme == "https"
               && AllowedHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
    }
}
