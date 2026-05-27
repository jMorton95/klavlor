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
        return await dataContext.CollectionLogItems
            .Select(c => c.ItemId)
            .ToListAsync();
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
