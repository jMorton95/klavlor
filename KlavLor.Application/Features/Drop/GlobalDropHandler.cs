using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Source;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Drop;

public sealed class GlobalDropHandler(IGlobalDropRepository repository, IMemoryCache cache)
{
    public const int SessionsLimit = 18;

    // Default sort for both tables: highest total value first.
    public const string DefaultSourceSort = "value";
    public const string DefaultCharacterSort = "value";
    public const SortDirection DefaultDirection = SortDirection.Descending;

    // The all-players aggregates are cached (versioned, 5-min TTL); the version is bumped per
    // item on loot ingest. The sortable tables are only cached for the default view (no search
    // term, default sort) — user-driven sorts/filters bypass the cache, matching how
    // GlobalSourceHandler treats the within-source item search.

    public Task<GlobalDropOverview?> GetOverview(string itemName)
        => Cached("overview", itemName, () => repository.GetOverview(itemName));

    public Task<DropSourceTable> GetSources(string itemName, string? sortBy, SortDirection? direction, string? term)
    {
        var sort = string.IsNullOrWhiteSpace(sortBy) ? DefaultSourceSort : sortBy;
        var dir = direction ?? DefaultDirection;
        return IsDefaultView(sort, dir, term, DefaultSourceSort)
            ? Cached("sources", itemName, () => repository.GetSources(itemName, sort, dir, null))
            : repository.GetSources(itemName, sort, dir, Normalize(term));
    }

    public Task<DropCharacterTable> GetCharacters(string itemName, string? sortBy, SortDirection? direction, string? term)
    {
        var sort = string.IsNullOrWhiteSpace(sortBy) ? DefaultCharacterSort : sortBy;
        var dir = direction ?? DefaultDirection;
        return IsDefaultView(sort, dir, term, DefaultCharacterSort)
            ? Cached("characters", itemName, () => repository.GetCharacters(itemName, sort, dir, null))
            : repository.GetCharacters(itemName, sort, dir, Normalize(term));
    }

    public Task<List<SourceTrendPoint>> GetMonthlyTrend(string itemName)
        => Cached("trend", itemName, () => repository.GetMonthlyTrend(itemName));

    public Task<List<DropSessionRow>> GetRecentSessions(string itemName)
        => Cached("sessions", itemName, () => repository.GetRecentSessions(itemName, SessionsLimit));

    private static bool IsDefaultView(string sort, SortDirection dir, string? term, string defaultSort)
        => string.IsNullOrWhiteSpace(term)
           && dir == DefaultDirection
           && string.Equals(sort, defaultSort, StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? term)
        => string.IsNullOrWhiteSpace(term) ? null : term.Trim();

    private async Task<T> Cached<T>(string method, string itemName, Func<Task<T>> factory)
    {
        var version = GlobalDropCache.GetVersion(cache, itemName);
        var key = GlobalDropCache.EntryKey(version, method, itemName);

        if (cache.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        var value = await factory();
        if (value is not null)
            cache.Set(key, value, GlobalDropCache.EntryTtl);
        return value;
    }
}
