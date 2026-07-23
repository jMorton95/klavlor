using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Loot.Leaderboard;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class LeaderboardItemExclusionRepository(
    DataContext dataContext,
    ILogger<LeaderboardItemExclusionRepository> logger) : ILeaderboardItemExclusionRepository
{
    public async Task<List<LeaderboardItemRow>> Search(string? term, int limit)
    {
        try
        {
            // Blank term shows the live exclusion list so the admin sees what's currently hidden.
            if (string.IsNullOrWhiteSpace(term))
            {
                return await dataContext.LeaderboardItemExclusions
                    .AsNoTracking()
                    .OrderBy(e => e.ItemName)
                    .Select(e => new LeaderboardItemRow(e.ItemName, 0, true))
                    .ToListAsync();
            }

            // Otherwise search items that actually appear in loot, flagging which are excluded.
            var pattern = $"%{term.Trim()}%";
            var rows = await dataContext.LootDrops
                .AsNoTracking()
                .Where(d => EF.Functions.ILike(d.Name, pattern))
                .GroupBy(d => d.Name)
                .Select(g => new { ItemName = g.Key, Count = g.LongCount() })
                .OrderByDescending(x => x.Count)
                .Take(limit)
                .ToListAsync();

            var excluded = (await dataContext.LeaderboardItemExclusions
                    .AsNoTracking()
                    .Select(e => e.ItemName)
                    .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return rows
                .Select(r => new LeaderboardItemRow(r.ItemName, r.Count, excluded.Contains(r.ItemName)))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search leaderboard item exclusions for term {Term}", term);
            throw new RepositoryException("Failed to search leaderboard item exclusions", ex);
        }
    }

    public async Task Exclude(string itemName)
    {
        try
        {
            var exists = await dataContext.LeaderboardItemExclusions.AnyAsync(e => e.ItemName == itemName);
            if (exists) return;
            dataContext.LeaderboardItemExclusions.Add(new LeaderboardItemExclusion { ItemName = itemName });
            await dataContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to exclude leaderboard item {Item}", itemName);
            throw new RepositoryException("Failed to exclude leaderboard item", ex);
        }
    }

    public async Task Include(string itemName)
    {
        try
        {
            await dataContext.LeaderboardItemExclusions
                .Where(e => e.ItemName == itemName)
                .ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to re-include leaderboard item {Item}", itemName);
            throw new RepositoryException("Failed to re-include leaderboard item", ex);
        }
    }

    public async Task<IReadOnlyCollection<string>> GetExcludedItemNames()
    {
        return await dataContext.LeaderboardItemExclusions
            .AsNoTracking()
            .Select(e => e.ItemName)
            .ToListAsync();
    }
}
