using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// Periodic sync of per-(source, item) drop rates from the OSRS Wiki into the
/// <c>DropRates</c> reference table. Mirrors <see cref="CollectionLogSyncService"/> in
/// shape: a 6-hour cycle (drop rates rarely change), a new DI scope per cycle, and
/// per-source transactional replace. Backlog-first ordering means sources we've
/// never synced are processed before refreshes of already-known ones.
/// </summary>
public sealed class DropRateSyncService(
    IServiceScopeFactory scopeFactory,
    ILogger<DropRateSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan PerSourcePause = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Defer past startup so we don't compete with migrations / cache priming.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(CycleInterval);

        try
        {
            do
            {
                await RunCycle(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Drop rate sync service failed unexpectedly");
        }
    }

    private async Task RunCycle(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDropRateRepository>();
            var runner = scope.ServiceProvider.GetRequiredService<IDropRateSyncRunner>();

            var knownSources = await repository.GetKnownSourceNames();
            if (knownSources.Count == 0)
            {
                logger.LogInformation("Drop rate sync: no source names in loot history yet, nothing to sync");
                return;
            }

            var lastSynced = await repository.GetLastSyncedAtBySource(knownSources);

            // Process never-synced sources first, then the oldest-synced ones — gives
            // newly-discovered bosses immediate coverage and ages-out stale rates evenly.
            var ordered = knownSources
                .OrderBy(s => lastSynced.TryGetValue(s, out var t) ? t : DateTimeOffset.MinValue)
                .ToList();

            var synced = 0;
            var emptied = 0;
            foreach (var sourceName in ordered)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var result = await runner.SyncSource(sourceName, stoppingToken);
                if (result.FoundWikiData) synced++;
                else emptied++;

                try
                {
                    await Task.Delay(PerSourcePause, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }

            logger.LogInformation("Drop rate sync: stored rates for {Synced} sources ({Emptied} sources had no DropsLine data)", synced, emptied);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Drop rate sync cycle failed");
        }
    }
}
