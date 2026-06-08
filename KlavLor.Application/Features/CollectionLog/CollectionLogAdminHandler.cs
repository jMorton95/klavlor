using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.CollectionLog;

// Admin curation of the collection-log blacklist. Every edit refreshes the in-memory
// cache (used during loot ingest) so exclusions take effect immediately, not just after
// the next hourly wiki sync.
public sealed class CollectionLogAdminHandler(
    ICollectionLogExclusionRepository exclusions,
    ICollectionLogItemRepository items,
    ICollectionLogCache cache,
    IMemoryCache aggregateCache)
{
    public const int SearchLimit = 40;

    public Task<List<ClogItemRow>> Search(string? term) => exclusions.Search(term, SearchLimit);

    public async Task<ClogItemRow> Exclude(int itemId, string itemName)
    {
        await exclusions.Exclude(itemId, itemName);
        await RefreshCache();
        return new ClogItemRow(itemId, itemName, true);
    }

    public async Task<ClogItemRow> Include(int itemId, string itemName)
    {
        await exclusions.Include(itemId);
        await RefreshCache();
        return new ClogItemRow(itemId, itemName, false);
    }

    // GetAllItemIds returns the effective set (synced items minus exclusions), so the
    // ingest cache mirrors the blacklist after every edit. Bumping the aggregate-cache
    // generation also drops the already-cached source/drop pages, whose collection-log
    // hovers and counts are derived from this set — without it an excluded item lingers
    // on those pages until their 5-minute entry TTL expires.
    private async Task RefreshCache()
    {
        cache.Replace(await items.GetAllItemIds());
        AggregateCacheGeneration.Bump(aggregateCache);
    }
}
