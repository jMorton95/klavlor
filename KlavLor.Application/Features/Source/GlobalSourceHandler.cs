using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Source;

public sealed class GlobalSourceHandler(IGlobalSourceRepository repository, IMemoryCache cache)
{
    public const int TopDropsLimit = 12;
    public const int PlayersLimit = 12;
    public const int ItemFrequencyLimit = 150;
    public const int RecentClogsLimit = 20;

    // The all-players aggregates are cached (versioned, 5-min TTL); the version is
    // bumped on loot ingest. The within-source search is user-driven and not cached.

    public Task<GlobalSourceOverview?> GetOverview(string sourceName)
        => Cached("overview", sourceName, () => repository.GetOverview(sourceName));

    public Task<List<GlobalSourceDrop>> GetTopDrops(string sourceName)
        => Cached("topdrops", sourceName, () => repository.GetTopDrops(sourceName, TopDropsLimit));

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
