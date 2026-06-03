using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;
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
            var runner = scope.ServiceProvider.GetRequiredService<ICollectionLogSyncRunner>();

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

            var stored = await runner.RunOnce(stoppingToken);
            if (stored == 0)
                logger.LogWarning("Collection log sync: wiki returned no items, keeping existing data");
            else
                logger.LogInformation("Collection log sync: stored {Count} collection-log items", stored);
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
