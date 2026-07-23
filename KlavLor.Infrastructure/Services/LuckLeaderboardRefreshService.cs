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
    ILogger<LuckLeaderboardRefreshService> logger) : BackgroundService
{
    // Must be at least this many multiples off the expected kill count to make a board.
    private const double MinMultiple = 2.0;

    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            do
            {
                await jobRuns.Track("Luck leaderboard refresh", () => RunCycle(stoppingToken));
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
            if (e.RarityDenominator is not { } den || den <= 0) continue;
            var expected = sourceLoot.ExpectedCompletions(source, e.ItemName, e.RarityNumerator ?? 1, den, e.Rolls);
            if (expected < 1) continue;                       // effectively guaranteed drop — not interesting
            var observed = e.KillCount ?? e.KillOrdinal ?? 0; // prefer the real RuneLite KC, fall back to logged ordinal
            if (observed <= 0) continue;

            if (observed <= expected)
            {
                var multiple = expected / observed;
                if (multiple >= MinMultiple)
                    entries.Add(Row(gen, charId, charName, source, e.ItemName,
                        LeaderboardBoard.Spoon, multiple, obtained: true, observed, expected, den));
            }
            else
            {
                var multiple = observed / expected;
                if (multiple >= MinMultiple)
                    entries.Add(Row(gen, charId, charName, source, e.ItemName,
                        LeaderboardBoard.DryStreak, multiple, obtained: true, observed, expected, den));
            }
        }

        // Not-yet-received clog items → an ongoing dry streak measured at the current kill count.
        foreach (var m in collection.MissingItems)
        {
            if (excludedItems.Contains(m.ItemName)) continue;
            if (m.RarityDenominator is not { } den || den <= 0) continue;
            var expected = sourceLoot.ExpectedCompletions(source, m.ItemName, m.RarityNumerator ?? 1, den, m.Rolls);
            if (expected < 1) continue;
            var observed = collection.CharacterKc;
            if (observed <= 0) continue;

            var multiple = observed / expected;
            if (multiple >= MinMultiple)
                entries.Add(Row(gen, charId, charName, source, m.ItemName,
                    LeaderboardBoard.DryStreak, multiple, obtained: false, observed, expected, den));
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
