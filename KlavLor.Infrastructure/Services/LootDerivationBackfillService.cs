using System.Text.Json;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

// Backfills the per-source derived metric (EffectiveKills) for special-source loot records
// that predate the strategy, and re-derives everything when SourceLootService.DerivationVersion
// is bumped. Designed to deploy cleanly onto a large production table:
//
//   - It only ever reads/writes records for sources that have a special strategy (Doom), so
//     the ordinary majority of LootRecords is never touched.
//   - A cheap EXISTS check gates each cycle, so once every special-source record is at the
//     current version the service does no work at all — the version marker is how it knows.
//   - Work is done in bounded batches with a per-cycle cap, and writes are set-based updates
//     of two columns only, so there are no long transactions, no table rewrites, no audit or
//     RowVersion churn, and the pass is fully idempotent and resumable if interrupted.
public sealed class LootDerivationBackfillService(
    IServiceScopeFactory scopeFactory,
    ILogger<LootDerivationBackfillService> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private const int MaxBatchesPerCycle = 100; // up to 50k rows per cycle, then resume next tick

    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
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
            logger.LogError(ex, "Loot derivation backfill service failed unexpectedly");
        }
    }

    private async Task RunCycle(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            logger.LogWarning("Loot derivation backfill still running; skipping this tick");
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ILootDerivationRepository>();
            var sourceLoot = scope.ServiceProvider.GetRequiredService<SourceLootService>();

            var sources = sourceLoot.SpecialSourceNames;
            var version = SourceLootService.DerivationVersion;
            if (sources.Count == 0) return;

            if (!await repository.HasRecordsNeedingDerivation(sources, version))
            {
                logger.LogDebug("Loot derivation backfill: nothing to do");
                return;
            }

            var processed = 0;
            var batches = 0;
            while (batches < MaxBatchesPerCycle && !ct.IsCancellationRequested)
            {
                var batch = await repository.GetBatchNeedingDerivation(sources, version, BatchSize);
                if (batch.Count == 0) break;

                var results = batch
                    .Select(r => new LootDerivationResult(r.Id, ComputeEffectiveKills(sourceLoot, r)))
                    .ToList();
                await repository.ApplyDerivations(results, version);

                processed += batch.Count;
                batches++;
            }

            if (processed > 0)
                logger.LogInformation("Loot derivation backfill: derived {Count} record(s) at version {Version}",
                    processed, version);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Loot derivation backfill cycle failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static int ComputeEffectiveKills(SourceLootService sourceLoot, LootDerivationRecord record)
    {
        var drops = ParseDrops(record.DropsJson);
        return sourceLoot.EffectiveKills(record.SourceName, drops);
    }

    private static List<ClaimDrop> ParseDrops(string dropsJson)
    {
        if (string.IsNullOrWhiteSpace(dropsJson)) return [];
        try
        {
            var drops = JsonSerializer.Deserialize<List<LootDrop>>(dropsJson);
            return drops is null ? [] : drops.Select(d => new ClaimDrop(d.Name, d.Quantity)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
