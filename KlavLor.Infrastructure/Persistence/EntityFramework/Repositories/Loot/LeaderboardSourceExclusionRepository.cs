using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Loot.Leaderboard;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class LeaderboardSourceExclusionRepository(
    DataContext dataContext,
    ILogger<LeaderboardSourceExclusionRepository> logger) : ILeaderboardSourceExclusionRepository
{
    public async Task<List<LeaderboardSourceRow>> Search(string? term, int limit)
    {
        try
        {
            // Blank term shows the live exclusion list so the admin sees what's currently hidden.
            if (string.IsNullOrWhiteSpace(term))
            {
                return await dataContext.LeaderboardSourceExclusions
                    .AsNoTracking()
                    .OrderBy(e => e.SourceName)
                    .Select(e => new LeaderboardSourceRow(e.SourceName, 0, true))
                    .ToListAsync();
            }

            // Otherwise search sources that actually have loot, flagging which are excluded.
            var pattern = $"%{term.Trim()}%";
            var rows = await dataContext.LootRecords
                .AsNoTracking()
                .Where(r => EF.Functions.ILike(r.SourceName, pattern))
                .GroupBy(r => r.SourceName)
                .Select(g => new { SourceName = g.Key, Count = g.LongCount() })
                .OrderByDescending(x => x.Count)
                .Take(limit)
                .ToListAsync();

            var excluded = (await dataContext.LeaderboardSourceExclusions
                .AsNoTracking()
                .Select(e => e.SourceName)
                .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return rows
                .Select(r => new LeaderboardSourceRow(r.SourceName, r.Count, excluded.Contains(r.SourceName)))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search leaderboard source exclusions for term {Term}", term);
            throw new RepositoryException("Failed to search leaderboard source exclusions", ex);
        }
    }

    public async Task Exclude(string sourceName)
    {
        try
        {
            var exists = await dataContext.LeaderboardSourceExclusions.AnyAsync(e => e.SourceName == sourceName);
            if (exists) return;
            dataContext.LeaderboardSourceExclusions.Add(new LeaderboardSourceExclusion { SourceName = sourceName });
            await dataContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to exclude leaderboard source {Source}", sourceName);
            throw new RepositoryException("Failed to exclude leaderboard source", ex);
        }
    }

    public async Task Include(string sourceName)
    {
        try
        {
            await dataContext.LeaderboardSourceExclusions
                .Where(e => e.SourceName == sourceName)
                .ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to re-include leaderboard source {Source}", sourceName);
            throw new RepositoryException("Failed to re-include leaderboard source", ex);
        }
    }

    public async Task<IReadOnlyCollection<string>> GetExcludedSourceNames()
    {
        return await dataContext.LeaderboardSourceExclusions
            .AsNoTracking()
            .Select(e => e.SourceName)
            .ToListAsync();
    }
}
