using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.CollectionLog;

/// <summary>
/// Backs the Collection Log area.
/// </summary>
/// <remarks>
/// THE RECONCILIATION RULE, in one place: TempleOSRS is authoritative for what a character OWNS;
/// our own loot data stays authoritative for kill counts, roll numbers and luck. Nothing here
/// derives a rate or a dryness figure, and nothing here writes to the loot tables.
///
/// The consequence a UI has to honour: an item Temple reports that we hold no drop for is still
/// shown as obtained — with its Temple date and no kill count — because it was almost certainly
/// obtained before loot tracking began. A missing kill count renders as unknown, never as zero, and
/// never suppresses the item.
///
/// Results are cached briefly and keyed off the shared aggregate generation, so a sync landing new
/// data invalidates them along with everything else.
/// </remarks>
public sealed class CollectionLogHandler(ICollectionLogQueryRepository repository, IMemoryCache cache)
{
    public const int SearchLimit = 60;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    public Task<List<CollectionLogStanding>> GetStandings()
        => Cached("standings", () => repository.GetStandings());

    public Task<CharacterCollectionLog?> GetCharacterLog(int gameCharacterId)
        => Cached($"character:{gameCharacterId}", () => repository.GetCharacterLog(gameCharacterId));

    public Task<List<CollectionLogItemState>> GetCategoryItems(int gameCharacterId, string categorySlug)
        => Cached($"category-items:{gameCharacterId}:{categorySlug}",
            () => repository.GetCategoryItems(gameCharacterId, Normalise(categorySlug)));

    public Task<CollectionLogCategoryComparison?> GetCategoryComparison(string categorySlug)
        => Cached($"category-compare:{categorySlug}", () => repository.GetCategoryComparison(Normalise(categorySlug)));

    public Task<CollectionLogItemComparison?> GetItemComparison(int itemId)
        => Cached($"item-compare:{itemId}", () => repository.GetItemComparison(itemId));

    // Search is user-driven and unbounded in its key space, so it is deliberately not cached.
    public Task<List<CollectionLogSearchRow>> SearchItems(string? term)
        => repository.SearchItems(string.IsNullOrWhiteSpace(term) ? null : term.Trim(), SearchLimit);

    /// <summary>Slugs come off a URL, so they are lowercased and bounded before hitting the database.</summary>
    private static string Normalise(string? slug)
        => (slug ?? "").Trim().ToLowerInvariant() is { Length: > 0 and <= 80 } s ? s : "";

    private async Task<T> Cached<T>(string key, Func<Task<T>> factory)
    {
        var generation = AggregateCacheGeneration.Get(cache);
        var cacheKey = $"clog:{generation}:{key}";

        if (cache.TryGetValue(cacheKey, out T? hit) && hit is not null)
            return hit;

        var value = await factory();
        if (value is not null)
            cache.Set(cacheKey, value, Ttl);
        return value;
    }
}
