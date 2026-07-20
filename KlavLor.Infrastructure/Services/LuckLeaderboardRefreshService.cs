using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Repositories;
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
                await RunCycle(stoppingToken);
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

    private async Task RunCycle(CancellationToken ct)
    {
        // Only one refresh at a time — skip the tick rather than queue a second run.
        if (!await _gate.WaitAsync(0, ct))
        {
            logger.LogWarning("Luck leaderboard refresh still running; skipping this tick");
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var lootLog = scope.ServiceProvider.GetRequiredService<ILootLogRepository>();
            var board = scope.ServiceProvider.GetRequiredService<ILuckLeaderboardRepository>();
            var sourceLoot = scope.ServiceProvider.GetRequiredService<SourceLootService>();

            var generation = await board.NextGeneration();
            var characters = await board.GetVisibleCharacters();
            var totalRows = 0;

            foreach (var (charId, charName) in characters)
            {
                if (ct.IsCancellationRequested) break;

                var sources = await board.GetSourcesForCharacter(charId);
                foreach (var source in sources)
                {
                    if (ct.IsCancellationRequested) break;

                    // Sources with a dedicated strategy (e.g. Doom of Mokhaiotl) don't follow the
                    // flat one-roll-per-kill maths this board assumes, so their luck would be wildly
                    // wrong. Skip them until the board is wired to compute luck through the strategy.
                    if (sourceLoot.HasSpecialModel(source)) continue;

                    var collection = await lootLog.GetSourceCollection(charId, source);
                    var entries = BuildEntries(generation, charId, charName, source, collection);
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
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Luck leaderboard refresh cycle failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static List<LuckLeaderboardEntry> BuildEntries(
        long gen, int charId, string charName, string source, SourceCollection collection)
    {
        var entries = new List<LuckLeaderboardEntry>();

        // Obtained clog items → a spoon if received faster than expected, a dry streak if slower.
        foreach (var e in collection.Entries)
        {
            if (e.RarityDenominator is not { } den || den <= 0) continue;
            var expected = ExpectedKc(e.RarityNumerator, den, e.Rolls);
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
            if (m.RarityDenominator is not { } den || den <= 0) continue;
            var expected = ExpectedKc(m.RarityNumerator, den, m.Rolls);
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

    // Expected kills to a first drop = 1 / effective per-kill probability.
    private static double ExpectedKc(int? numerator, int denominator, int rolls)
    {
        var p = Math.Max(1, rolls) * (double)(numerator ?? 1) / denominator;
        return p <= 0 ? double.MaxValue : 1.0 / p;
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
