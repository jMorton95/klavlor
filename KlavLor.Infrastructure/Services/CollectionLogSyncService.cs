using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// Hourly sync of the OSRS collection-log item list from the wiki into the reference table and
/// the in-memory <see cref="ICollectionLogCache"/>. On startup the cache is primed from the
/// persisted table first, so classification works immediately even before the wiki call returns.
/// </summary>
public sealed class CollectionLogSyncService(
    IServiceScopeFactory scopeFactory,
    ICollectionLogCache cache,
    ILogger<CollectionLogSyncService> logger) : BackgroundService
{
    private bool _primed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for app startup
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        try
        {
            // Run immediately on first tick, then hourly
            do
            {
                await RunCycle(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Collection log sync service failed unexpectedly");
        }
    }

    private async Task RunCycle(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ICollectionLogItemRepository>();
            var wikiClient = scope.ServiceProvider.GetRequiredService<IOsrsWikiClient>();

            // Prime the cache from the persisted table once, so a restart classifies correctly
            // before the (slower) wiki fetch completes.
            if (!_primed)
            {
                var existing = await repository.GetAllItemIds();
                if (existing.Count > 0)
                {
                    cache.Replace(existing);
                    logger.LogInformation("Collection log cache primed from database with {Count} items", existing.Count);
                }
                _primed = true;
            }

            var fetched = await wikiClient.FetchCollectionLogItems();
            if (fetched.Count == 0)
            {
                logger.LogWarning("Collection log sync: wiki returned no items, keeping existing data");
                return;
            }

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
            cache.Replace(items.Select(i => i.ItemId));

            logger.LogInformation("Collection log sync: stored {Count} collection-log items", items.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Collection log sync cycle failed");
        }
    }
}
