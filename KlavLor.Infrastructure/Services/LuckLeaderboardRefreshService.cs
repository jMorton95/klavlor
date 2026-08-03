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

    // Items the player has NOT obtained yet get a lower bar: they join the dry board the moment
    // they pass the expected kill count once. A 1/100 item still missing at 101 kills is a real
    // (if mild) dry streak and shows as 1x dry, rather than staying invisible until 2x. Obtained
    // items keep MinMultiple — a drop that came in slightly late isn't worth a board slot.
    private const double MinMissingMultiple = 1.0;

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
    // own drop rate in kills is ranked by denominator/1000; everything else must clear
    // `minMultiple` — MinMultiple for items already obtained, MinMissingMultiple for ones the
    // player is still chasing.
    // internal so the dry-board entry rules can be pinned by tests without standing up the service.
    internal static double? DryMultiple(double observed, double expected, int rarityDenominator, double minMultiple)
    {
        if (observed <= expected) return null;
        var actual = observed / expected;
        if (rarityDenominator >= RareGrindDenominator && observed >= rarityDenominator)
            // Rank just below its integer tier: shave 0.01 so a 1/3000 grind floors to tier 2 and
            // sits under every natural 3.x streak rather than topping tier 3. Genuine dryness wins.
            return Math.Max(rarityDenominator / 1000.0 - 0.01, actual);
        return actual >= minMultiple ? actual : null;
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
        // Same shared normalisation the character page uses. Without it the raw run list contains a
        // zero depth for every record the backfill hasn't stamped, and for every non-depth source,
        // which would zero out the observed figure and silently drop entries from the board.
        var runs = sourceLoot.NormaliseRuns(source, collection.Runs);
        var allDepths = runs.Select(r => r.Depth).ToList();

        // Obtained clog items → a spoon if received faster than expected, a dry streak if slower.
        foreach (var e in collection.Entries)
        {
            if (excludedItems.Contains(e.ItemName)) continue;
            var den = e.RarityDenominator ?? 0;   // 0 for depth-modelled sources (Doom) with no flat rate

            // Depth-modelled sources are scored in DELVE LEVELS on both sides (see
            // DoomLootStrategy.ExpectedCompletionsForRuns): the rates are per level, and one run can
            // be four delves deep or twenty, so a run count can't be compared against them. The
            // window stops at the drop itself, so a shallow grind isn't judged as if every run had
            // been a deep delve, and the observed figure is the delves done up to that same point.
            var window = DepthsUpTo(runs, e.FirstRecordId);
            var observed = window.Count > 0
                ? window.Sum()
                : e.KillCount ?? e.KillOrdinal ?? 0; // prefer the real RuneLite KC, else logged ordinal

            var expected = sourceLoot.ExpectedCompletions(
                source, e.ItemName, e.RarityNumerator ?? 1, den, e.Rolls, window);
            if (expected < 1 || expected >= double.MaxValue) continue; // no usable rate / guaranteed drop

            // Rarity-based bottom-end filter only applies when there's a real denominator.
            if (den > 0)
            {
                var perDropValue = e.TotalDrops > 0 ? e.TotalValue / e.TotalDrops : 0;
                if (IsUninteresting(den, perDropValue)) continue;
            }
            if (observed <= 0) continue;

            if (observed <= expected)
            {
                var multiple = expected / observed;
                if (multiple >= MinMultiple)
                    entries.Add(Row(gen, charId, charName, source, e.ItemName,
                        LeaderboardBoard.Spoon, multiple, obtained: true, observed, expected, den));
            }
            else if (DryMultiple(observed, expected, den, MinMultiple) is { } dryMultiple)
            {
                entries.Add(Row(gen, charId, charName, source, e.ItemName,
                    LeaderboardBoard.DryStreak, dryMultiple, obtained: true, observed, expected, den));
            }
        }

        // Not-yet-received clog items → an ongoing dry streak measured at the current kill count.
        // Every one of these joins the board as soon as the character has put in enough kills to
        // have expected the drop once (MinMissingMultiple), not only at 2x: still missing a 1/100
        // item at 101 kills is a genuine, if mild, streak and lands as 1x dry.
        foreach (var m in collection.MissingItems)
        {
            if (excludedItems.Contains(m.ItemName)) continue;
            var den = m.RarityDenominator ?? 0;   // 0 for depth-modelled sources (Doom)
            var expected = sourceLoot.ExpectedCompletions(source, m.ItemName, m.RarityNumerator ?? 1, den, m.Rolls, allDepths);
            if (expected < 1 || expected >= double.MaxValue) continue;
            // Missing items carry no received value, so the bottom-end filter still drops items
            // more common than 1/100 — otherwise a 1x bar would flood the board with commons.
            // Note 1/100 itself passes, matching the worked example above.
            if (den > 0 && IsUninteresting(den, 0)) continue;
            var observed = collection.CharacterKc;
            if (observed <= 0) continue;

            if (DryMultiple(observed, expected, den, MinMissingMultiple) is { } dryMultiple)
                entries.Add(Row(gen, charId, charName, source, m.ItemName,
                    LeaderboardBoard.DryStreak, dryMultiple, obtained: false, observed, expected, den));
        }

        return entries;
    }

    // Depths of the runs up to and including the one an item first dropped on. Empty for ordinary
    // sources (no derived depths), which makes the facade fall back to the flat rate.
    private static List<int> DepthsUpTo(IReadOnlyList<SourceRun> runs, int lastRecordId)
    {
        if (runs.Count == 0) return [];
        for (var i = 0; i < runs.Count; i++)
        {
            if (runs[i].RecordId == lastRecordId)
                return runs.Take(i + 1).Select(r => r.Depth).ToList();
        }
        return runs.Select(r => r.Depth).ToList();
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
