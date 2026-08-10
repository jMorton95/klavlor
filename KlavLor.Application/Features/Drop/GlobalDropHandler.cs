using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Drop;

public sealed class GlobalDropHandler(
    IGlobalDropRepository repository,
    SourceLootService sourceLoot,
    IMemoryCache cache)
{
    public const int SessionsLimit = 18;

    // Sources default to most-dropped first: the grid answers "where does this come from", and
    // quantity is the honest answer to that. Value re-orders it by price, which for a single item is
    // just quantity again unless the price changed mid-history.
    public const string DefaultSourceSort = "qty";
    public const string DefaultCharacterSort = "value";
    public const SortDirection DefaultDirection = SortDirection.Descending;

    public Task<DropCharacterSources?> GetCharacterSources(string itemName, int gameCharacterId)
        => Cached($"character-sources:{gameCharacterId}", itemName,
            () => repository.GetCharacterSources(itemName, gameCharacterId));

    // The all-players aggregates are cached (versioned, 5-min TTL); the version is bumped per
    // item on loot ingest. The sortable tables are only cached for the default view (no search
    // term, default sort) — user-driven sorts/filters bypass the cache, matching how
    // GlobalSourceHandler treats the within-source item search.

    public Task<GlobalDropOverview?> GetOverview(string itemName)
        => Cached("overview", itemName, () => repository.GetOverview(itemName));

    // Each row's rate goes through SourceLootService so an admin rate modifier — a global baseline
    // — and the source's loot model are reflected here too, per (source, item) pair. No per-run
    // depth exists in a global aggregate, so depth-modelled sources show no rate here rather than
    // inventing an assumed depth.
    public async Task<DropSourceTable> GetSources(string itemName, string? sortBy, SortDirection? direction, string? term)
    {
        var sort = string.IsNullOrWhiteSpace(sortBy) ? DefaultSourceSort : sortBy;
        var dir = direction ?? DefaultDirection;
        var table = await (IsDefaultView(sort, dir, term, DefaultSourceSort)
            ? Cached("sources", itemName, () => repository.GetSources(itemName, sort, dir, null))
            : repository.GetSources(itemName, sort, dir, Normalize(term)));

        return table with
        {
            Rows = table.Rows
                .Select(r => r with
                {
                    EffectiveRarity = sourceLoot
                        .EffectiveRate(r.SourceName, itemName, r.RarityNumerator, r.RarityDenominator, r.Rolls)?.Rarity
                })
                .ToList()
        };
    }

    public Task<DropCharacterTable> GetCharacters(string itemName, string? sortBy, SortDirection? direction, string? term)
    {
        var sort = string.IsNullOrWhiteSpace(sortBy) ? DefaultCharacterSort : sortBy;
        var dir = direction ?? DefaultDirection;
        return IsDefaultView(sort, dir, term, DefaultCharacterSort)
            ? Cached("characters", itemName, () => repository.GetCharacters(itemName, sort, dir, null))
            : repository.GetCharacters(itemName, sort, dir, Normalize(term));
    }

    // characterId scopes the panel to one character (the per-character drop page) and is part of
    // the cache key, so the scoped and unscoped views never share an entry.
    public Task<List<DropTrendPoint>> GetMonthlyTrend(string itemName, int? characterId = null)
        => Cached(Method("trend", characterId), itemName, () => repository.GetMonthlyTrend(itemName, characterId));

    public Task<List<DropSessionRow>> GetRecentSessions(string itemName, int? characterId = null)
        => Cached(Method("sessions", characterId), itemName,
            () => repository.GetRecentSessions(itemName, SessionsLimit, characterId));

    private static string Method(string name, int? characterId)
        => characterId is { } id ? $"{name}:{id}" : name;

    private static bool IsDefaultView(string sort, SortDirection dir, string? term, string defaultSort)
        => string.IsNullOrWhiteSpace(term)
           && dir == DefaultDirection
           && string.Equals(sort, defaultSort, StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? term)
        => string.IsNullOrWhiteSpace(term) ? null : term.Trim();

    private async Task<T> Cached<T>(string method, string itemName, Func<Task<T>> factory)
    {
        var generation = AggregateCacheGeneration.Get(cache);
        var version = GlobalDropCache.GetVersion(cache, itemName);
        var key = GlobalDropCache.EntryKey(generation, version, method, itemName);

        if (cache.TryGetValue(key, out T? hit) && hit is not null)
            return hit;

        var value = await factory();
        if (value is not null)
            cache.Set(key, value, GlobalDropCache.EntryTtl);
        return value;
    }
}
