using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.Services;

// Rebuilds the luck leaderboard hourly. Streams character-by-character, source-by-source,
// reusing the proven GetSourceCollection query for each unit and clearing the change tracker
// after every insert, so peak memory is one source's clog items rather than the whole matrix
// — favours a small footprint over speed on modest infrastructure. A semaphore guarantees a
// single refresh at a time even if a slow run overruns the next tick.
public sealed class LuckLeaderboardRefreshService(
    IServiceScopeFactory scopeFactory,
    IJobRunRecorder jobRuns,
    IJobScheduler scheduler,
    ILogger<LuckLeaderboardRefreshService> logger) : BackgroundService
{
    // Poll once a minute; actually run at most hourly (or immediately on an admin manual trigger).
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    // Must be at least this many multiples off the expected kill count to make a board.
    private const double MinMultiple = 2.0;

    // Rare "special curse" items (1/2000 or rarer): once a player has done at least the item's
    // own drop rate in kills, it's ranked by that rarity — denominator/1000 — so a 1/5000 grind
    // reads as 5x and a 1/3000 as 3x, never below the genuine dryness.
    private const int RareGrindDenominator = 2000;

    // Bottom end: items more common than 1/100 that are also low value aren't interesting.
    private const int CommonDenominator = 100;
    private const long MinInterestingValue = 100_000;

    private static bool IsUninteresting(int rarityDenominator, long perDropValue) =>
        rarityDenominator < CommonDenominator && perDropValue < MinInterestingValue;

    // Dry-board multiple for an item, or null if it doesn't qualify. A rare-grind item past its
    // own drop rate in kills is ranked by denominator/1000; everything else needs MinMultiple.
    private static double? DryMultiple(double observed, double expected, int rarityDenominator)
    {
        if (observed <= expected) return null;
        var actual = observed / expected;
        if (rarityDenominator >= RareGrindDenominator && observed >= rarityDenominator)
            return Math.Max(rarityDenominator / 1000.0, actual);
        return actual >= MinMultiple ? actual : null;
    }

    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                if (await scheduler.TryClaimRun(BackgroundJobNames.LuckLeaderboardRefresh, RunInterval))
                    await jobRuns.Track(BackgroundJobNames.LuckLeaderboardRefresh, () => RunCycle(stoppingToken));
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Luck leaderboard service failed unexpectedly");
        }
    }

    private async Task<JobRunResult> RunCycle(CancellationToken ct)
    {
        // Only one refresh at a time — skip the tick rather than queue a second run.
        if (!await _gate.WaitAsync(0, ct))
        {
            logger.LogWarning("Luck leaderboard refresh still running; skipping this tick");
            return new JobRunResult(JobRunOutcome.NoWork, 0, "skipped; previous run still in progress");
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var lootLog = scope.ServiceProvider.GetRequiredService<ILootLogRepository>();
            var board = scope.ServiceProvider.GetRequiredService<ILuckLeaderboardRepository>();
            var sourceLoot = scope.ServiceProvider.GetRequiredService<SourceLootService>();
            var exclusions = scope.ServiceProvider.GetRequiredService<ILeaderboardSourceExclusionRepository>();
            var itemExclusions = scope.ServiceProvider.GetRequiredService<ILeaderboardItemExclusionRepository>();

            // Admin-blacklisted sources (wrong stored rates) are dropped from both boards.
            var excluded = (await exclusions.GetExcludedSourceNames())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Admin-blacklisted items — hidden across every source (e.g. shared rare-drop-table drops).
            var excludedItems = (await itemExclusions.GetExcludedItemNames())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Surfaced in the job-run history so a "why isn't my exclusion working" question is
            // answerable without DB access: shows whether the list is read and what's on it.
            logger.LogInformation("Luck leaderboard refresh: {Count} excluded source(s): {Sources}",
                excluded.Count, excluded.Count == 0 ? "(none)" : string.Join(", ", excluded));

            var generation = await board.NextGeneration();
            var characters = await board.GetVisibleCharacters();
            var totalRows = 0;
            var excludedSkipped = 0;

            foreach (var (charId, charName) in characters)
            {
                if (ct.IsCancellationRequested) break;

                var sources = await board.GetSourcesForCharacter(charId);
                foreach (var source in sources)
                {
                    if (ct.IsCancellationRequested) break;

                    // Sources whose luck the board can't compute from a flat rate (e.g. Doom's
                    // delve model) opt out via the strategy; raids stay in but get their rates
                    // normalised inside BuildEntries via ExpectedCompletions.
                    if (!sourceLoot.IncludeInLeaderboard(source)) continue;

                    // Admin-excluded sources (wrong stored drop rates) never appear on the boards.
                    if (excluded.Contains(source)) { excludedSkipped++; continue; }

                    var collection = await lootLog.GetSourceCollection(charId, source);
                    var entries = BuildEntries(generation, charId, charName, source, collection, sourceLoot, excludedItems);
                    if (entries.Count > 0)
                    {
                        await board.InsertEntries(entries);
                        totalRows += entries.Count;
                    }
                }
            }

            await board.PublishGeneration(generation);
            logger.LogInformation(
                "Luck leaderboard refreshed: generation {Gen}, {Rows} rows across {Chars} characters",
                generation, totalRows, characters.Count);
            return JobRunResult.Ok(totalRows,
                $"generation {generation}, {characters.Count} characters, {excluded.Count} excluded source(s) ({excludedSkipped} skipped)");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Luck leaderboard refresh cycle failed");
            return JobRunResult.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static List<LuckLeaderboardEntry> BuildEntries(
        long gen, int charId, string charName, string source, SourceCollection collection,
        SourceLootService sourceLoot, IReadOnlySet<string> excludedItems)
    {
        var entries = new List<LuckLeaderboardEntry>();

        // Obtained clog items → a spoon if received faster than expected, a dry streak if slower.
        foreach (var e in collection.Entries)
        {
            if (excludedItems.Contains(e.ItemName)) continue;
            var den = e.RarityDenominator ?? 0;   // 0 for depth-modelled sources (Doom) with no flat rate
            var expected = sourceLoot.ExpectedCompletions(source, e.ItemName, e.RarityNumerator ?? 1, den, e.Rolls, collection.CharacterDepth);
            if (expected < 1 || expected >= double.MaxValue) continue; // no usable rate / guaranteed drop
            // Rarity-based bottom-end filter only applies when there's a real denominator.
            if (den > 0)
            {
                var perDropValue = e.TotalDrops > 0 ? e.TotalValue / e.TotalDrops : 0;
                if (IsUninteresting(den, perDropValue)) continue;
            }
            var observed = e.KillCount ?? e.KillOrdinal ?? 0; // prefer the real RuneLite KC, fall back to logged ordinal
            if (observed <= 0) continue;

            if (observed <= expected)
            {
                var multiple = expected / observed;
                if (multiple >= MinMultiple)
                    entries.Add(Row(gen, charId, charName, source, e.ItemName,
                        LeaderboardBoard.Spoon, multiple, obtained: true, observed, expected, den));
            }
            else if (DryMultiple(observed, expected, den) is { } dryMultiple)
            {
                entries.Add(Row(gen, charId, charName, source, e.ItemName,
                    LeaderboardBoard.DryStreak, dryMultiple, obtained: true, observed, expected, den));
            }
        }

        // Not-yet-received clog items → an ongoing dry streak measured at the current kill count.
        foreach (var m in collection.MissingItems)
        {
            if (excludedItems.Contains(m.ItemName)) continue;
            var den = m.RarityDenominator ?? 0;   // 0 for depth-modelled sources (Doom)
            var expected = sourceLoot.ExpectedCompletions(source, m.ItemName, m.RarityNumerator ?? 1, den, m.Rolls, collection.CharacterDepth);
            if (expected < 1 || expected >= double.MaxValue) continue;
            // Missing items carry no received value; the bottom-end filter (rarity-based) still drops commons.
            if (den > 0 && IsUninteresting(den, 0)) continue;
            var observed = collection.CharacterKc;
            if (observed <= 0) continue;

            if (DryMultiple(observed, expected, den) is { } dryMultiple)
                entries.Add(Row(gen, charId, charName, source, m.ItemName,
                    LeaderboardBoard.DryStreak, dryMultiple, obtained: false, observed, expected, den));
        }

        return entries;
    }

    private static LuckLeaderboardEntry Row(
        long gen, int charId, string charName, string source, string item,
        LeaderboardBoard board, double multiple, bool obtained, int observedKc, double expectedKc, int rarityDen) =>
        new()
        {
            Generation = gen,
            GameCharacterId = charId,
            CharacterName = charName,
            SourceName = source,
            ItemName = item,
            Board = board,
            Tier = (int)Math.Floor(multiple),
            Multiple = multiple,
            Obtained = obtained,
            ObservedKc = observedKc,
            ExpectedKc = expectedKc,
            RarityDenominator = rarityDen
        };
}
