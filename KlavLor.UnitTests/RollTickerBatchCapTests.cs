using KlavLor.Application.Features.Loot.Ingest;
using KlavLor.Domain.Entities;

namespace KlavLor.UnitTests;

// klavlor-sync tails, so a player returning after a long break syncs thousands of kills as LIVE,
// 250 to a batch. Uncapped, every one of those is an SSE frame and a DOM swap for every person
// watching — for a banner that holds 40 and shows about 16, where all but the last handful are
// evicted before anyone could read them.
//
// The cap is what stops that flood, and it is invisible when it regresses: the ticker would simply
// get janky for viewers during someone else's catch-up sync.
public sealed class RollTickerBatchCapTests
{
    private static readonly DateTimeOffset T = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static (LootRecord Record, GameCharacter? Character) Kill(int id, int minutesFromStart) =>
        (new LootRecord
        {
            Id = id,
            SourceName = "Vorkath",
            OccurredAt = T.AddMinutes(minutesFromStart),
            DropsJson = "[]"
        }, null);

    [Fact]
    public void A_backlog_is_trimmed_to_the_newest_rolls()
    {
        var batch = Enumerable.Range(1, 250).Select(i => Kill(i, i)).ToList();

        var trimmed = LootIngestHandler.TrimToNewestRolls(batch);

        Assert.Equal(LootIngestHandler.MaxRollsPublishedPerBatch, trimmed.Count);

        // The NEWEST are what survive — the dropped ones are the oldest, which would have been
        // evicted from the banner before a viewer could read them anyway.
        Assert.Equal(250, trimmed[^1].Record.Id);
        Assert.Equal(250 - LootIngestHandler.MaxRollsPublishedPerBatch + 1, trimmed[0].Record.Id);
    }

    [Fact]
    public void An_ordinary_sync_passes_through_untouched()
    {
        // The common case by far: a handful of kills. The cap must not be doing anything here.
        var batch = Enumerable.Range(1, 4).Select(i => Kill(i, i)).ToList();

        var trimmed = LootIngestHandler.TrimToNewestRolls(batch);

        Assert.Equal(4, trimmed.Count);
        Assert.Equal([1, 2, 3, 4], trimmed.Select(x => x.Record.Id).ToArray());
    }

    [Fact]
    public void Rolls_come_back_oldest_first_whatever_order_they_arrived_in()
    {
        // The banner prepends, so publishing chronologically leaves the newest leftmost. A batch
        // that arrives out of order must still publish in time order.
        var batch = new[] { Kill(3, 30), Kill(1, 10), Kill(2, 20) };

        var trimmed = LootIngestHandler.TrimToNewestRolls(batch);

        Assert.Equal([1, 2, 3], trimmed.Select(x => x.Record.Id).ToArray());
    }

    [Fact]
    public void Kills_sharing_a_timestamp_keep_a_stable_order()
    {
        // Same tie-break as every other roll-ordering query: (OccurredAt, Id). Without it two kills
        // at one timestamp could swap between runs.
        var batch = new[] { Kill(7, 5), Kill(5, 5), Kill(6, 5) };

        var trimmed = LootIngestHandler.TrimToNewestRolls(batch);

        Assert.Equal([5, 6, 7], trimmed.Select(x => x.Record.Id).ToArray());
    }

    [Fact]
    public void An_empty_batch_publishes_nothing()
        => Assert.Empty(LootIngestHandler.TrimToNewestRolls([]));
}
