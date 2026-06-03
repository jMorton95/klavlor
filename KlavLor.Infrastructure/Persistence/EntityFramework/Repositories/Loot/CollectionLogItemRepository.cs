using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class CollectionLogItemRepository(DataContext dataContext, ILogger<CollectionLogItemRepository> logger) : ICollectionLogItemRepository
{
    public async Task<IReadOnlyList<int>> GetAllItemIds()
    {
        // Effective set: synced items minus the admin blacklist, so the in-memory cache
        // (and anything primed from it) never treats an excluded item as a clog item.
        return await dataContext.CollectionLogItems
            .Where(c => !dataContext.CollectionLogExclusions.Any(e => e.ItemId == c.ItemId))
            .Select(c => c.ItemId)
            .ToListAsync();
    }

    public async Task<(int Count, DateTimeOffset? LastSynced)> GetStatus()
    {
        var agg = await dataContext.CollectionLogItems
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), LastSynced = (DateTimeOffset?)g.Max(c => c.SyncedAt) })
            .FirstOrDefaultAsync();
        return (agg?.Count ?? 0, agg?.LastSynced);
    }

    public async Task ReplaceAll(IReadOnlyCollection<CollectionLogItem> items)
    {
        // Guard: never wipe the reference set with an empty payload (e.g. a failed wiki fetch).
        if (items.Count == 0) return;

        await using var transaction = await dataContext.Database.BeginTransactionAsync();
        try
        {
            await dataContext.CollectionLogItems.ExecuteDeleteAsync();
            dataContext.CollectionLogItems.AddRange(items);
            await dataContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Failed to replace collection log items");
            throw new RepositoryException("Failed to replace collection log items", ex);
        }
    }
}
