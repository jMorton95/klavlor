using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Leaderboard;
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

    // A single entry bar for both boards, replacing the old split of 1.75x for items you already
    // own and 1.0x for ones you're still chasing, plus the "rare grind" floor that lifted rare
    // items up the board by overwriting their multiple with denominator/1000.
    //
    // Anything past its expected roll count qualifies, provided the item is rare enough to be worth
    // a slot. Rarity no longer needs to buy its way in through the multiple, because LuckScore ranks
    // by rarity directly — so the floor's synthetic values, and the display code that had to undo
    // them, are both gone.
    private const double MinMultipleForBoard = 1.0;

    // Bottom end: a drop this common only earns a slot if a single receipt is worth real money.
    private const long MinInterestingValue = 100_000;

    // Whether an item is worth a board slot at all, judged on the rolls it actually takes rather
    // than the stored wiki denominator — the latter is 0 for a depth-modelled source and ignores
    // multi-roll tables. perDropValue is 0 for an item never received, which is correct: an ongoing
    // streak has to earn its place on rarity alone.
    private static bool WorthABoardSlot(double expectedRolls, long perDropValue) =>
        expectedRolls >= LuckScore.MinExpectedRollsForBoard || perDropValue >= MinInterestingValue;

    // The multiple for a board entry, or null if it doesn't qualify. Plain arithmetic now: no floor,
    // no synthetic ranking value, so Multiple is always the honest ratio and LuckScore does the
    // ranking. internal so the entry rules can be pinned by tests without standing up the service.
    internal static double? BoardMultiple(double observed, double expected)
    {
        if (observed <= expected) return null;
        var actual = observed / expected;
        return actual > MinMultipleForBoard ? actual : null;
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
            var lootLog = scope.ServiceProvider.GetRequiredService<ILootSourceDetailRepository>();
            var board = scope.ServiceProvider.GetRequiredService<ILuckLeaderboardRepository>();
            var sourceLoot = scope.ServiceProvider.GetRequiredService<SourceLootService>();
            var exclusions = scope.ServiceProvider.GetRequiredService<ILeaderboardSourceExclusionRepository>();
            var itemExclusions = scope.ServiceProvider.GetRequiredService<ILeaderboardItemExclusionRepository>();
            var delveDepths = scope.ServiceProvider.GetRequiredService<ICharacterDelveDepthRepository>();

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
                    // Same admin delve-depth override the character page honours, so the board and
                    // the page can never disagree about how deep a player's runs were.
                    var overrideDepth = await delveDepths.GetAverageDepth(charId, source);
                    var entries = BuildEntries(
                        generation, charId, charName, source, collection, sourceLoot, excludedItems, overrideDepth);
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
        SourceLootService sourceLoot, IReadOnlySet<string> excludedItems, int? overrideDepth)
    {
        var entries = new List<LuckLeaderboardEntry>();
        // Same shared normalisation the character page uses. Without it the raw run list contains a
        // zero depth for every record the backfill hasn't stamped, and for every non-depth source,
        // which would zero out the observed figure and silently drop entries from the board.
        var runs = sourceLoot.NormaliseRuns(source, collection.Runs, overrideDepth);
        var allDepths = runs.Select(r => r.Depth).ToList();

        // Obtained clog items → a spoon if received faster than expected, a dry streak if slower.
        foreach (var e in collection.Entries)
        {
            if (excludedItems.Contains(e.ItemName)) continue;
            var den = e.RarityDenominator ?? 0;   // 0 for depth-modelled sources (Doom) with no flat rate

            // RUNS on both sides. ExpectedCompletionsForRuns returns expected RUNS, so the observed
            // side must be a run count too — this used to sum the window's depths, i.e. delves, and
            // compare that against a run-denominated expectation. At depth 8 that reported every
            // Doom drop as eight times drier than it was: an Eye of ayak at 293 runs against 148
            // expected showed as 2,344 versus 148.
            //
            // The window still stops at the drop itself, so the expectation is built from the depths
            // of the runs up to that point rather than from a whole history the player only reached
            // later — a shallow grind isn't judged as if every run had been a deep delve.
            var window = DepthsUpTo(runs, e.FirstRecordId);
            var observed = window.Count > 0
                ? window.Count
                : e.KillCount ?? e.KillOrdinal ?? 0; // prefer the real RuneLite KC, else logged ordinal

            var expected = sourceLoot.ExpectedCompletions(
                source, e.ItemName, e.RarityNumerator ?? 1, den, e.Rolls, window);
            if (expected < 1 || expected >= double.MaxValue) continue; // no usable rate / guaranteed drop

            var perDropValue = e.TotalDrops > 0 ? e.TotalValue / e.TotalDrops : 0;
            if (!WorthABoardSlot(expected, perDropValue)) continue;
            if (observed <= 0) continue;

            if (observed <= expected)
            {
                // Spoon: received faster than expected, so the multiple inverts and bigger is luckier.
                var multiple = expected / observed;
                if (multiple > MinMultipleForBoard)
                    entries.Add(Row(gen, charId, charName, source, e.ItemName,
                        LeaderboardBoard.Spoon, multiple, obtained: true, observed, expected, den));
            }
            else if (BoardMultiple(observed, expected) is { } dryMultiple)
            {
                entries.Add(Row(gen, charId, charName, source, e.ItemName,
                    LeaderboardBoard.DryStreak, dryMultiple, obtained: true, observed, expected, den));
            }
        }

        // Not-yet-received clog items → an ongoing dry streak measured at the current roll count.
        // Same single bar as an obtained item: anything past the expected roll count qualifies, so
        // still missing a 1/100 item at 101 rolls is a genuine, if mild, streak and lands as 1x dry.
        foreach (var m in collection.MissingItems)
        {
            if (excludedItems.Contains(m.ItemName)) continue;
            var den = m.RarityDenominator ?? 0;   // 0 for depth-modelled sources (Doom)
            var expected = sourceLoot.ExpectedCompletions(source, m.ItemName, m.RarityNumerator ?? 1, den, m.Rolls, allDepths);
            if (expected < 1 || expected >= double.MaxValue) continue;
            // No received value to fall back on, so an ongoing streak must clear the rarity bar.
            if (!WorthABoardSlot(expected, 0)) continue;
            var observed = collection.CharacterKc;
            if (observed <= 0) continue;

            if (BoardMultiple(observed, expected) is { } dryMultiple)
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
            Score = LuckScore.For(multiple, expectedKc),
            Multiple = multiple,
            Obtained = obtained,
            ObservedKc = observedKc,
            ExpectedKc = expectedKc,
            RarityDenominator = rarityDen
        };
}
