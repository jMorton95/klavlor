using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.CachedImages;

internal sealed class CachedImageRepository(DataContext dataContext, ILogger<CachedImageRepository> logger) : ICachedImageRepository
{
    public async Task<CachedImage?> GetById(int id)
    {
        try
        {
            return await dataContext.CachedImages.FirstOrDefaultAsync(c => c.Id == id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get cached image by id {Id}", id);
            throw new RepositoryException("Failed to get cached image", ex);
        }
    }

    public async Task<CachedImage?> GetBySourceUrl(string sourceUrl)
    {
        try
        {
            return await dataContext.CachedImages.FirstOrDefaultAsync(c => c.SourceUrl == sourceUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get cached image by source URL");
            throw new RepositoryException("Failed to get cached image by source URL", ex);
        }
    }

    public async Task<CachedImage> Save(CachedImage image)
    {
        try
        {
            if (image.Id == 0)
                dataContext.CachedImages.Add(image);
            else
                dataContext.CachedImages.Update(image);

            await dataContext.SaveChangesAsync();
            return image;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save cached image");
            throw new RepositoryException("Failed to save cached image", ex);
        }
    }

    public async Task<List<string>> GetAllSourceUrls()
    {
        try
        {
            return await dataContext.CachedImages.Select(c => c.SourceUrl).ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get all cached image source URLs");
            throw new RepositoryException("Failed to get cached image source URLs", ex);
        }
    }
}
