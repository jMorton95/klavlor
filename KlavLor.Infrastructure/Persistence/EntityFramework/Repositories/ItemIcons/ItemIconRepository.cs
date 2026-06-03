using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.ItemIcons;

internal sealed class ItemIconRepository(DataContext dataContext, ILogger<ItemIconRepository> logger) : IItemIconRepository
{
    public async Task<List<ItemIcon>> GetFailedIcons(int limit)
    {
        return await dataContext.ItemIcons
            .Where(i => i.CachedImageId == null && i.FailCount >= 3)
            .OrderBy(i => i.ItemName)
            .Take(limit)
            .ToListAsync();
    }

    public async Task ResetFailure(int id)
    {
        // Clear the failure state so the backfill service re-attempts on its next cycle.
        await dataContext.ItemIcons
            .Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.FailCount, 0)
                .SetProperty(i => i.LastAttemptAt, (DateTimeOffset?)null));
    }

    public async Task<IconStats> GetStats()
    {
        var total = await dataContext.ItemIcons.CountAsync();
        var cached = await dataContext.ItemIcons.CountAsync(i => i.CachedImageId != null);
        var failed = await dataContext.ItemIcons.CountAsync(i => i.CachedImageId == null && i.FailCount >= 3);
        var last = await dataContext.ItemIcons.MaxAsync(i => (DateTimeOffset?)i.LastAttemptAt);
        return new IconStats(total, cached, total - cached - failed, failed, last);
    }

    public async Task<ItemIcon?> GetByItemName(string itemName)
    {
        try
        {
            var normalized = itemName.Trim().ToLower();
            return await dataContext.ItemIcons
                .FirstOrDefaultAsync(i => i.ItemName.ToLower() == normalized);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get item icon by name {Name}", itemName);
            throw new RepositoryException("Failed to get item icon", ex);
        }
    }

    public async Task<List<(string Name, int ItemId)>> FindUncataloguedItems(int limit)
    {
        try
        {
            var results = await dataContext.Database
                .SqlQueryRaw<UncataloguedItem>(
                    """
                    SELECT DISTINCT drop_elem->>'Name' AS "ItemName",
                           MIN((drop_elem->>'ItemId')::int) AS "ItemId"
                    FROM "LootRecords" lr,
                         jsonb_array_elements(lr."DropsJson") AS drop_elem
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "ItemIcons" ii WHERE LOWER(ii."ItemName") = LOWER(drop_elem->>'Name')
                    )
                    GROUP BY drop_elem->>'Name'
                    LIMIT {0}
                    """, limit)
                .ToListAsync();

            return results.Select(r => (r.ItemName, r.ItemId)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to find uncatalogued items");
            throw new RepositoryException("Failed to find uncatalogued items", ex);
        }
    }

    public async Task<List<ItemIcon>> GetPendingIcons(int limit)
    {
        try
        {
            return await dataContext.ItemIcons
                .Where(i => i.CachedImageId == null && i.FailCount < 3
                    && (i.LastAttemptAt == null || i.LastAttemptAt < DateTimeOffset.UtcNow.AddMinutes(-30)))
                .OrderBy(i => i.FailCount)
                .ThenBy(i => i.Id)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get pending icons");
            throw new RepositoryException("Failed to get pending icons", ex);
        }
    }

    public async Task Save(ItemIcon icon)
    {
        try
        {
            if (icon.Id == 0)
                dataContext.ItemIcons.Add(icon);
            else
                dataContext.ItemIcons.Update(icon);

            await dataContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save item icon for {Name}", icon.ItemName);
            throw new RepositoryException("Failed to save item icon", ex);
        }
    }

    public async Task SaveRange(List<ItemIcon> icons)
    {
        try
        {
            var newIcons = icons.Where(i => i.Id == 0).ToList();
            var existingIcons = icons.Where(i => i.Id != 0).ToList();

            foreach (var icon in newIcons)
            {
                await dataContext.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO "ItemIcons" ("ItemName", "ItemId", "CachedImageId", "FailCount", "LastAttemptAt")
                    VALUES ({icon.ItemName}, {icon.ItemId}, {icon.CachedImageId}, {icon.FailCount}, {icon.LastAttemptAt})
                    ON CONFLICT ("ItemName") DO NOTHING
                    """);
            }

            if (existingIcons.Count > 0)
            {
                dataContext.ItemIcons.UpdateRange(existingIcons);
                await dataContext.SaveChangesAsync();
            }
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save item icons batch");
            throw new RepositoryException("Failed to save item icons batch", ex);
        }
    }
}

internal sealed class UncataloguedItem
{
    public string ItemName { get; set; } = "";
    public int ItemId { get; set; }
}
