using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public interface IImageCacheService
{
    /// <summary>
    /// Checks if the image is already cached. If so, returns the cached image.
    /// Otherwise fetches from the source URL, resizes/re-encodes per the profile,
    /// stores in DB, and returns the cached image.
    /// Returns null if fetching fails.
    /// </summary>
    Task<CachedImage?> GetOrCache(string sourceUrl, ImageProfile profile);

    /// <summary>
    /// Parses a data URI, resizes/re-encodes per the profile, stores in cache.
    /// Returns null if the data URI is invalid.
    /// </summary>
    Task<CachedImage?> GetOrCacheFromDataUri(string dataUri, ImageProfile profile);
}
