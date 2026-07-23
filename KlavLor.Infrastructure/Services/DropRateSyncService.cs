using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
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
    IJobRunRecorder jobRuns,
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
                await jobRuns.Track("Drop rate sync", () => RunCycle(stoppingToken));
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

    private async Task<JobRunResult> RunCycle(CancellationToken stoppingToken)
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
                return new JobRunResult(JobRunOutcome.NoWork, 0, "no source names in loot history yet");
            }

            var lastSynced = await repository.GetLastSyncedAtBySource(knownSources);

            // Process never-synced sources first, then the oldest-synced ones — gives
            // newly-discovered bosses immediate coverage and ages-out stale rates evenly.
            var ordered = knownSources
                .OrderBy(s => lastSynced.TryGetValue(s, out var t) ? t : DateTimeOffset.MinValue)
                .ToList();

            // Snapshot coverage before the cycle so we can report how many sources this pass
            // newly covered — the signal that the post-deploy backfill is filling the gaps
            // (e.g. herb/seed multi-source rates that had no data under the old scraper).
            var countsBefore = await repository.GetRateCountsBySource();

            var synced = 0;
            var emptied = 0;
            var failed = 0;
            foreach (var sourceName in ordered)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var result = await runner.SyncSource(sourceName, stoppingToken);
                switch (result.Outcome)
                {
                    case DropRateSyncOutcome.Synced: synced++; break;
                    case DropRateSyncOutcome.NoData: emptied++; break;
                    default: failed++; break;
                }

                try
                {
                    await Task.Delay(PerSourcePause, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }

            var countsAfter = await repository.GetRateCountsBySource();
            bool HadRates(IReadOnlyDictionary<string, int> counts, string s) => counts.TryGetValue(s, out var c) && c > 0;
            var newlyCovered = knownSources.Count(s => !HadRates(countsBefore, s) && HadRates(countsAfter, s));
            var stillMissing = knownSources.Count(s => !HadRates(countsAfter, s));

            logger.LogInformation(
                "Drop rate sync: {Synced} sources stored rates, {NewlyCovered} newly covered, {Emptied} had no wiki data, {Failed} fetch failures (kept existing); {StillMissing} of {Total} sources still without rates",
                synced, newlyCovered, emptied, failed, stillMissing, knownSources.Count);
            return JobRunResult.Ok(synced,
                $"{newlyCovered} newly covered, {emptied} no wiki data, {failed} failures, {stillMissing}/{knownSources.Count} still missing");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Drop rate sync cycle failed");
            return JobRunResult.Failed(ex.Message);
        }
    }
}
