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
        // A few collection-log names are shared by more than one item id (e.g. "Rum" appears
        // twice in the clog), so keying a dictionary straight off the name throws "same key".
        // This resolution is best-effort, so fetch the rows and pick a deterministic winner
        // (lowest item id) per name rather than letting a duplicate crash the whole sync.
        var clogRows = await dataContext.CollectionLogItems
            .Where(c => names.Contains(c.Name.ToLower()))
            .Select(c => new { c.Name, c.ItemId })
            .ToListAsync();
        var clogByLowerName = clogRows
            .GroupBy(c => c.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.ItemId).First().ItemId);

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

    public async Task<DropRate?> GetRate(string sourceName, string itemName)
    {
        var lowerItem = itemName.ToLower();
        var matches = await dataContext.DropRates
            .Where(d => d.SourceName == sourceName && d.ItemName.ToLower() == lowerItem)
            .ToListAsync();

        // Prefer a row whose rarity reduced to a usable N/D form so luck can be computed.
        return matches.FirstOrDefault(d => d.RarityNumerator != null && d.RarityDenominator != null)
               ?? matches.FirstOrDefault();
    }

    public async Task<(int SourceCount, int RateCount, DateTimeOffset? LastSynced)> GetStatus()
    {
        var rateCount = await dataContext.DropRates.CountAsync();
        var sourceCount = await dataContext.DropRates.Select(d => d.SourceName).Distinct().CountAsync();
        var lastSynced = await dataContext.DropRates.MaxAsync(d => (DateTimeOffset?)d.SyncedAt);
        return (sourceCount, rateCount, lastSynced);
    }

    public async Task MarkNoWikiData(string sourceName)
    {
        var exists = await dataContext.DropRateMisses.AnyAsync(d => d.SourceName == sourceName);
        if (exists) return;

        dataContext.DropRateMisses.Add(new DropRateMiss { SourceName = sourceName });
        await dataContext.SaveChangesAsync();
    }

    public async Task ClearNoWikiData(string sourceName)
    {
        await dataContext.DropRateMisses
            .Where(d => d.SourceName == sourceName)
            .ExecuteDeleteAsync();
    }

    public async Task<IReadOnlyList<string>> GetNoWikiDataSources()
    {
        return await dataContext.DropRateMisses
            .Select(d => d.SourceName)
            .ToListAsync();
    }
}
