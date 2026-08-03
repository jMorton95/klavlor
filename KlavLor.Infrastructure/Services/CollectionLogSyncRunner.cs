using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// One collection-log wiki sync: fetch the item list, replace the reference table, and
/// refresh the in-memory cache from the effective (post-blacklist) set. Shared by the
/// hourly <see cref="CollectionLogSyncService"/> and the admin "sync now" action.
/// </summary>
internal sealed class CollectionLogSyncRunner(
    ICollectionLogItemRepository repository,
    IOsrsWikiClient wikiClient,
    ICollectionLogCache cache) : ICollectionLogSyncRunner
{
    public async Task<int> RunOnce(CancellationToken cancellationToken = default)
    {
        var fetched = await wikiClient.FetchCollectionLogItems();
        if (fetched.Count == 0)
            return 0; // never wipe the reference set on a failed/empty fetch

        var items = fetched
            .GroupBy(i => i.Id)              // defend against duplicate ids in the source
            .Select(g => g.First())
            .Select(i => new CollectionLogItem
            {
                ItemId = i.Id,
                Name = i.Name,
                Tabs = i.Tabs?.ToArray(),
                SyncedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        await repository.ReplaceAll(items);
        // Re-read the effective set (synced minus admin blacklist) so exclusions hold.
        cache.Replace(await repository.GetAllEntries());
        return items.Count;
    }
}
