using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Maintenance;

// Aggregates the state of the data-sync pipelines for the admin health panel, and runs
// the collection-log sync on demand.
public sealed class SyncStatusHandler(
    ICollectionLogItemRepository clogItems,
    IDropRateRepository dropRates,
    IItemIconRepository itemIcons,
    ISourceIconRepository sourceIcons,
    ICachedImageRepository cachedImages,
    ICollectionLogSyncRunner clogRunner)
{
    public async Task<SyncStatus> Get()
    {
        // Sequential — one scoped DbContext, never concurrent.
        var clog = await clogItems.GetStatus();
        var dr = await dropRates.GetStatus();
        var item = await itemIcons.GetStats();
        var source = await sourceIcons.GetStats();
        var images = await cachedImages.Count();

        return new SyncStatus(
            clog.Count, clog.LastSynced,
            dr.SourceCount, dr.RateCount, dr.LastSynced,
            item, source, images);
    }

    public async Task<SyncStatus> RunClogSyncNow()
    {
        await clogRunner.RunOnce();
        return await Get();
    }
}
