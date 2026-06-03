using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.CollectionLog;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class CollectionLogExclusionRepository(DataContext dataContext, ILogger<CollectionLogExclusionRepository> logger)
    : ICollectionLogExclusionRepository
{
    public async Task<List<ClogItemRow>> Search(string? term, int limit)
    {
        try
        {
            // Blank term: show what's currently excluded so the admin sees the live blacklist.
            if (string.IsNullOrWhiteSpace(term))
            {
                return await dataContext.CollectionLogExclusions
                    .AsNoTracking()
                    .OrderBy(e => e.ItemName)
                    .Select(e => new ClogItemRow(e.ItemId, e.ItemName, true))
                    .ToListAsync();
            }

            var pattern = $"%{term.Trim()}%";
            return await dataContext.CollectionLogItems
                .AsNoTracking()
                .Where(c => EF.Functions.ILike(c.Name, pattern))
                .OrderBy(c => c.Name)
                .Take(limit)
                .Select(c => new ClogItemRow(
                    c.ItemId,
                    c.Name,
                    dataContext.CollectionLogExclusions.Any(e => e.ItemId == c.ItemId)))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search collection-log items for blacklist (term {Term})", term);
            throw new RepositoryException("Failed to search collection-log items", ex);
        }
    }

    public async Task Exclude(int itemId, string itemName)
    {
        try
        {
            var exists = await dataContext.CollectionLogExclusions.AnyAsync(e => e.ItemId == itemId);
            if (exists) return;

            dataContext.CollectionLogExclusions.Add(new CollectionLogExclusion { ItemId = itemId, ItemName = itemName });
            await dataContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to exclude collection-log item {ItemId}", itemId);
            throw new RepositoryException("Failed to exclude collection-log item", ex);
        }
    }

    public async Task Include(int itemId)
    {
        try
        {
            await dataContext.CollectionLogExclusions
                .Where(e => e.ItemId == itemId)
                .ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove collection-log exclusion {ItemId}", itemId);
            throw new RepositoryException("Failed to remove collection-log exclusion", ex);
        }
    }
}
