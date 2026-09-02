using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// "Is this drop a collection-log item" is asked in FOUR places, and they have to agree.
//
// ICollectionLogCache.IsCollectionLogItem matches on ID **or NAME**, deliberately: the id RuneLite
// reports for an item is not always the id the wiki sync recorded (variants, renames, an untradeable
// logged with no id at all), and the name is what survives that. The query that decides which rows
// appear on the collection panel matches both ways too.
//
// Two SQL sites did not. They matched on id alone, so an item that qualified by NAME appeared in the
// table as received and then:
//   * its KC cell carried cursor-help with an EMPTY hover popover — the "?" pointer and no tooltip,
//     because the drop-events query found none of its receipts; and
//   * it was left out of the "X of Y collection log items" count, which then disagreed with the very
//     table printed underneath it.
[Collection("postgres")]
public sealed class SourceCollectionClogMatchTests(PostgresFixture fx)
{
    private const string Source = "CLM_Sire";

    // What the wiki sync recorded...
    private const int ClogItemId = 92_001;
    // ...and the different id the drop was actually logged under. Same name, which is the whole point.
    private const int LoggedItemId = 92_002;
    private const string SharedName = "CLM Bludgeon claw";

    // The ordinary case, matching on id, as a control: the fix must not have traded one for the other.
    private const int IdMatchedItem = 92_003;
    private const string IdMatchedName = "CLM Abyssal head";

    private static LootSourceDetailRepository Repo(DataContext ctx) =>
        new(ctx, NullLogger<LootSourceDetailRepository>.Instance, new FakeClogCache(), new FakeItemValueCache());

    [Fact]
    public async Task A_name_matched_clog_item_still_gets_its_drop_events()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "clog-name");
        var t = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

        Seed.AddClogItem(ctx, ClogItemId, SharedName, Source);
        Seed.AddClogItem(ctx, IdMatchedItem, IdMatchedName, Source);

        // Two receipts of the name-matched item, one of the id-matched control.
        Seed.AddKill(ctx, userId, charId, Source, t, 10, [new(SharedName, LoggedItemId, 1, 0)]);
        Seed.AddKill(ctx, userId, charId, Source, t.AddDays(1), 20, [new(SharedName, LoggedItemId, 1, 0)]);
        Seed.AddKill(ctx, userId, charId, Source, t.AddDays(2), 30, [new(IdMatchedName, IdMatchedItem, 1, 0)]);
        await ctx.SaveChangesAsync();

        var collection = await Repo(ctx).GetSourceCollection(charId, Source);

        var named = collection.Entries.Single(e => e.ItemName == SharedName);
        Assert.Equal(2, named.TotalDrops);

        // THE REGRESSION: DropEvents came back null, so the KC column rendered cursor-help over
        // nothing at all.
        Assert.NotNull(named.DropEvents);
        Assert.Equal(2, named.DropEvents!.Count);

        // Newest first, carrying the KC each one landed on.
        Assert.Equal([20, 10], named.DropEvents.Select(d => d.KillCount).ToArray());

        // The control still works — this is the path that was never broken.
        var byId = collection.Entries.Single(e => e.ItemName == IdMatchedName);
        Assert.NotNull(byId.DropEvents);
        Assert.Equal([30], byId.DropEvents!.Select(d => d.KillCount).ToArray());
    }

    [Fact]
    public async Task A_name_matched_clog_item_counts_toward_the_unlocked_total()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "clog-count");
        var t = new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);

        // A distinct source so the shared container's other rows cannot inflate the count.
        const string source = "CLM_Sire_Count";
        Seed.AddClogItem(ctx, 92_011, "CLM Count claw", source);
        Seed.AddClogItem(ctx, 92_012, "CLM Count head", source);
        Seed.AddClogItem(ctx, 92_013, "CLM Count orphan", source);

        // One received by name (logged under a different id), one by id. The third is never received.
        Seed.AddKill(ctx, userId, charId, source, t, 1, [new("CLM Count claw", 92_099, 1, 0)]);
        Seed.AddKill(ctx, userId, charId, source, t.AddHours(1), 2, [new("CLM Count head", 92_012, 1, 0)]);
        await ctx.SaveChangesAsync();

        var popover = await Repo(ctx).GetSourcePopover(charId, source);

        // THE REGRESSION: this counted only the id match and reported 1 of 3, while the table below
        // it listed both items as received.
        Assert.Equal(2, popover.ClogUnlocked);
        Assert.Equal(3, popover.ClogTotal);
    }
}
