using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
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
            var wikiClient = scope.ServiceProvider.GetRequiredService<IOsrsWikiClient>();

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

                var mapping = SourceNameAliases.Resolve(sourceName);
                var wikiRates = await wikiClient.FetchDropRatesForSource(mapping.PageTitle);
                if (mapping.SectionFilter is not null)
                {
                    // Filter to rows whose section heading contains the filter token so a
                    // shared wiki page (e.g. The Gauntlet) feeds the right variant rates
                    // to each in-game source name.
                    wikiRates = wikiRates
                        .Where(r => r.Section is not null && r.Section.Contains(mapping.SectionFilter, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
                if (wikiRates.Count == 0)
                {
                    emptied++;
                    continue;
                }

                var rates = wikiRates
                    // Same item can appear twice (e.g. normal-mode + hard-mode sections);
                    // collapse by name keeping the rarer (smaller probability) entry so the
                    // unique index doesn't reject the batch.
                    .GroupBy(r => r.ItemName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderBy(EffectiveProbability).First())
                    .Select(r => new DropRate
                    {
                        SourceName = sourceName,
                        ItemName = r.ItemName,
                        Rarity = r.Rarity,
                        RarityNumerator = r.Numerator,
                        RarityDenominator = r.Denominator,
                        Rolls = r.Rolls,
                        Quantity = r.Quantity,
                        Notes = r.Section
                    })
                    .ToList();

                await repository.ReplaceForSource(sourceName, rates);
                synced++;

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

    // Sort key for dedup: rarer drops (lower probability) win. Rates with unparseable
    // denominators sink to the bottom since we can't compare them numerically.
    private static double EffectiveProbability(WikiDropRate r)
    {
        if (r.Denominator is null or 0 || r.Numerator is null) return double.MaxValue;
        return (double)r.Numerator.Value / r.Denominator.Value;
    }
}
