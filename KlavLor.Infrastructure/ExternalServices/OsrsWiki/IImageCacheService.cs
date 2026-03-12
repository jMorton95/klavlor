using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public interface IImageCacheService
{
    /// <summary>
    /// Checks if the image is already cached. If so, returns the cached image.
    /// Otherwise fetches from the source URL, stores in DB, and returns the cached image.
    /// Returns null if fetching fails.
    /// </summary>
    Task<CachedImage?> GetOrCache(string sourceUrl);

    /// <summary>
    /// Parses a data URI, stores the image data in the cache, and returns the cached image.
    /// Returns null if the data URI is invalid.
    /// </summary>
    Task<CachedImage?> GetOrCacheFromDataUri(string dataUri);
}
