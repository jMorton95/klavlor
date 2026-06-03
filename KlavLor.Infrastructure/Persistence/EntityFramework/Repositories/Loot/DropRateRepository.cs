using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class DropRateRepository(DataContext dataContext, ILogger<DropRateRepository> logger) : IDropRateRepository
{
    public async Task ReplaceForSource(string sourceName, IReadOnlyCollection<DropRate> rates)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return;
        if (rates.Count == 0) return; // never wipe existing rows on an empty-fetch (failed parse)

        // Resolve each row's ItemId opportunistically against the clog reference set,
        // by case-insensitive name. Joined in a single pass to keep the write a small
        // bounded number of round-trips even for sources with hundreds of drops.
        var names = rates.Select(r => r.ItemName.ToLower()).ToList();
        var clogByLowerName = await dataContext.CollectionLogItems
            .Where(c => names.Contains(c.Name.ToLower()))
            .ToDictionaryAsync(c => c.Name.ToLowerInvariant(), c => c.ItemId);

        foreach (var rate in rates)
        {
            rate.SourceName = sourceName;
            if (rate.ItemId is null && clogByLowerName.TryGetValue(rate.ItemName.ToLowerInvariant(), out var id))
                rate.ItemId = id;
            rate.SyncedAt = DateTimeOffset.UtcNow;
        }

        await using var transaction = await dataContext.Database.BeginTransactionAsync();
        try
        {
            await dataContext.DropRates
                .Where(d => d.SourceName == sourceName)
                .ExecuteDeleteAsync();
            dataContext.DropRates.AddRange(rates);
            await dataContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Failed to replace drop rates for source {Source}", sourceName);
            throw new RepositoryException("Failed to replace drop rates", ex);
        }
    }

    public async Task<IReadOnlyList<string>> GetKnownSourceNames()
    {
        return await dataContext.LootRecords
            .Select(r => r.SourceName)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetLastSyncedAtBySource(IReadOnlyCollection<string> knownSourceNames)
    {
        if (knownSourceNames.Count == 0)
            return new Dictionary<string, DateTimeOffset>();

        var rows = await dataContext.DropRates
            .Where(d => knownSourceNames.Contains(d.SourceName))
            .GroupBy(d => d.SourceName)
            .Select(g => new { SourceName = g.Key, LastSynced = g.Max(d => d.SyncedAt) })
            .ToListAsync();

        return rows.ToDictionary(r => r.SourceName, r => r.LastSynced);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetRateCountsBySource()
    {
        var rows = await dataContext.DropRates
            .GroupBy(d => d.SourceName)
            .Select(g => new { SourceName = g.Key, Count = g.Count() })
            .ToListAsync();

        return rows.ToDictionary(r => r.SourceName, r => r.Count);
    }

    public async Task<IReadOnlyList<CollectionLogItem>> GetClogItemsMissingRates(int limit)
    {
        return await dataContext.CollectionLogItems
            .Where(c => !dataContext.DropRates.Any(d => d.ItemId == c.ItemId))
            .OrderBy(c => c.Name)
            .Take(limit)
            .ToListAsync();
    }

    public Task<int> CountClogItemsMissingRates()
    {
        return dataContext.CollectionLogItems
            .CountAsync(c => !dataContext.DropRates.Any(d => d.ItemId == c.ItemId));
    }

    public async Task<(int SourceCount, int RateCount, DateTimeOffset? LastSynced)> GetStatus()
    {
        var rateCount = await dataContext.DropRates.CountAsync();
        var sourceCount = await dataContext.DropRates.Select(d => d.SourceName).Distinct().CountAsync();
        var lastSynced = await dataContext.DropRates.MaxAsync(d => (DateTimeOffset?)d.SyncedAt);
        return (sourceCount, rateCount, lastSynced);
    }
}
