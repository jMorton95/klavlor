using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Source;

public sealed class GlobalSourceHandler(
    IGlobalSourceRepository repository,
    SourceLootService sourceLoot,
    IMemoryCache cache)
{
    public const int TopDropsLimit = 12;
    public const int PlayersLimit = 12;
    public const int ItemFrequencyLimit = 150;
    public const int RecentClogsLimit = 20;

    // The all-players aggregates are cached (versioned, 5-min TTL); the version is
    // bumped on loot ingest. The within-source search is user-driven and not cached.

    public Task<GlobalSourceOverview?> GetOverview(string sourceName)
        => Cached("overview", sourceName, () => repository.GetOverview(sourceName));

    // Rates shown here go through SourceLootService so an admin rate modifier — a global baseline
    // by definition — and the source's loot model are reflected on the all-players page too, not
    // just on a character's own source page. No per-run depth exists in a global aggregate, so
    // depth-modelled sources simply show no rate here rather than inventing an assumed depth.
    public async Task<List<GlobalSourceDrop>> GetTopDrops(string sourceName)
    {
        var drops = await Cached("topdrops", sourceName, () => repository.GetTopDrops(sourceName, TopDropsLimit));
        return drops
            .Select(d => d with
            {
                EffectiveRarity = sourceLoot
                    .EffectiveRate(sourceName, d.ItemName, d.RarityNumerator, d.RarityDenominator, d.Rolls)?.Rarity
            })
            .ToList();
    }

    public Task<List<SourcePlayerRow>> GetPlayers(string sourceName)
        => Cached("players", sourceName, () => repository.GetPlayers(sourceName, PlayersLimit));

    public Task<List<SourceClogEvent>> GetRecentClogs(string sourceName)
        => Cached("clogs", sourceName, () => repository.GetRecentClogs(sourceName, RecentClogsLimit));

    public Task<List<SourceItemFrequency>> GetItemFrequency(string sourceName, string? term)
        => string.IsNullOrWhiteSpace(term)
            ? Cached("items", sourceName, () => repository.GetItemFrequency(sourceName, null, ItemFrequencyLimit))
            : repository.GetItemFrequency(sourceName, term, ItemFrequencyLimit);

    public Task<List<SourceTrendPoint>> GetMonthlyTrend(string sourceName)
        => Cached("trend", sourceName, () => repository.GetMonthlyTrend(sourceName));

    private async Task<T> Cached<T>(string method, string sourceName, Func<Task<T>> factory)
    {
        var generation = AggregateCacheGeneration.Get(cache);
        var version = GlobalSourceCache.GetVersion(cache, sourceName);
        var key = GlobalSourceCache.EntryKey(generation, version, method, sourceName);

        if (cache.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        var value = await factory();
        if (value is not null)
            cache.Set(key, value, GlobalSourceCache.EntryTtl);
        return value;
    }
}
