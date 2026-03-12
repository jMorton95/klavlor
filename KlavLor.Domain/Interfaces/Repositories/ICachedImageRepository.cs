using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface ICachedImageRepository
{
    Task<CachedImage?> GetById(int id);
    Task<CachedImage?> GetBySourceUrl(string sourceUrl);
    Task<CachedImage> Save(CachedImage image);
    Task<List<string>> GetAllSourceUrls();
}
