using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public sealed class ImageCacheService(
    HttpClient httpClient,
    ICachedImageRepository cachedImageRepository,
    ILogger<ImageCacheService> logger) : IImageCacheService
{
    public async Task<CachedImage?> GetOrCache(string sourceUrl)
    {
        var existing = await cachedImageRepository.GetBySourceUrl(sourceUrl);
        if (existing is not null)
            return existing;

        try
        {
            using var response = await httpClient.GetAsync(sourceUrl);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch image from {Url}: {StatusCode}", sourceUrl, response.StatusCode);
                return null;
            }

            var imageData = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";

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
}
