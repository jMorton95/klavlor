using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// An admin can take one record out of the luck maths without deleting it
// (LootRecord.ExcludedFromLuck). The case is a receipt we cannot rate honestly rather than a kill
// that never happened - a crystal armour seed logged against Hunllef - so the rules are asymmetric
// and worth pinning against real SQL:
//
//   - the record's DROPS stop being receipts, so the item leaves the obtained side
//   - the record still counts as a ROLL, because the kill happened
//   - the item does NOT reappear as a still-being-chased missing item, which would put an ongoing
//     dry streak on the board for a drop the player has actually had
//
// GetSourceCollection is the single query behind both the luck leaderboard and the character page's
// collection panel, so these assertions cover both surfaces at once.
//
// The fixture's database is shared across the whole collection, so every test here scopes itself
// with its own source name and its own clog item ids - CollectionLogItems is keyed on ItemId, and
// two tests registering the same one collide.
[Collection("postgres")]
public sealed class RecordLuckExclusionTests(PostgresFixture fx)
{
    private static LootSourceDetailRepository Repo(DataContext ctx, params int[] clogIds) =>
        new(ctx, NullLogger<LootSourceDetailRepository>.Instance, new FakeClogCache(clogIds), new FakeItemValueCache());

    [Fact]
    public async Task An_excluded_record_stops_being_a_receipt_but_still_counts_as_a_roll()
    {
        const string source = "RLE1_Corrupted Hunllef";
        const string seed = "RLE1 crystal armour seed";
        const int seedId = 910_101;
        const string shards = "RLE1 crystal shards";
        const int shardsId = 910_102;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "rle-one");
        var t = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

        Seed.AddClogItem(ctx, seedId, seed, source);
        // Entries lists collection-log items only, so the control item has to be one too for its
        // survival to be worth asserting.
        Seed.AddClogItem(ctx, shardsId, shards, source);

        // Three kills at the source, the middle one carrying the seed.
        Seed.AddKill(ctx, userId, charId, source, t, 1, [new(shards, shardsId, 20, 100, IsFirstTime: true)]);
        var withSeed = Seed.AddKill(ctx, userId, charId, source, t.AddMinutes(5), 2,
            [new(seed, seedId, 1, 0, IsFirstTime: true)]);
        Seed.AddKill(ctx, userId, charId, source, t.AddMinutes(10), 3, [new(shards, shardsId, 15, 100)]);
        await ctx.SaveChangesAsync();

        // Before: the seed is an obtained collection-log entry, and there are three runs.
        var before = await Repo(ctx, seedId, shardsId).GetSourceCollection(charId, source);
        Assert.Contains(before.Entries, e => e.ItemName == seed);
        Assert.DoesNotContain(before.MissingItems, m => m.ItemName == seed);
        Assert.Equal(3, before.Runs.Count);

        withSeed.ExcludedFromLuck = true;
        await ctx.SaveChangesAsync();

        var after = await Repo(ctx, seedId, shardsId).GetSourceCollection(charId, source);

        // The receipt is gone from the luck maths...
        Assert.DoesNotContain(after.Entries, e => e.ItemName == seed);
        // ...without coming back as an ongoing dry streak...
        Assert.DoesNotContain(after.MissingItems, m => m.ItemName == seed);
        // ...and the kill itself still counts, on both the run list and the KC.
        Assert.Equal(3, after.Runs.Count);
        Assert.Equal(before.CharacterKc, after.CharacterKc);
        // Items on other records are untouched.
        Assert.Contains(after.Entries, e => e.ItemName == shards);
    }

    [Fact]
    public async Task Excluding_one_receipt_leaves_the_others_and_moves_the_first_receipt_on()
    {
        const string source = "RLE2_Corrupted Hunllef";
        const string seed = "RLE2 crystal armour seed";
        const int seedId = 910_201;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "rle-two");
        var t = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);

        Seed.AddClogItem(ctx, seedId, seed, source);

        // Two receipts of the same item. Excluding the first must leave the item obtained, now
        // credited to the second - the leaderboard windows its expectation on the first receipt's
        // record, so this is the assertion that stops an exclusion silently keeping the old window.
        var first = Seed.AddKill(ctx, userId, charId, source, t, 1, [new(seed, seedId, 1, 0, IsFirstTime: true)]);
        Seed.AddKill(ctx, userId, charId, source, t.AddMinutes(5), 2, [new("RLE2 bones", 910_202, 1, 100)]);
        var second = Seed.AddKill(ctx, userId, charId, source, t.AddMinutes(10), 3, [new(seed, seedId, 1, 0)]);
        await ctx.SaveChangesAsync();

        var before = await Repo(ctx, seedId).GetSourceCollection(charId, source);
        var beforeEntry = before.Entries.Single(e => e.ItemName == seed);
        Assert.Equal(2, beforeEntry.TotalDrops);
        Assert.Equal(first.Id, beforeEntry.FirstRecordId);

        first.ExcludedFromLuck = true;
        await ctx.SaveChangesAsync();

        var after = await Repo(ctx, seedId).GetSourceCollection(charId, source);
        var afterEntry = after.Entries.Single(e => e.ItemName == seed);
        Assert.Equal(1, afterEntry.TotalDrops);
        // The surviving receipt is now the first one that counts, so the luck window moves with it
        // rather than staying anchored to a receipt nobody is claiming any more.
        Assert.Equal(second.Id, afterEntry.FirstRecordId);
        Assert.Equal(3, afterEntry.KillCount);
    }

    [Fact]
    public async Task The_admin_toggle_is_idempotent_and_reversible()
    {
        const string source = "RLE3_Corrupted Hunllef";

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "rle-three");
        var t = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

        var rec = Seed.AddKill(ctx, userId, charId, source, t, 1,
            [new("RLE3 crystal armour seed", 910_301, 1, 0, IsFirstTime: true)]);
        await ctx.SaveChangesAsync();

        ILootRecordAuditRepository audit =
            new LootRecordAuditRepository(ctx, NullLogger<LootRecordAuditRepository>.Instance);

        // Setting it twice reports success both times rather than failing the second call: the
        // toggle sends the state it wants, so a double click asks for what is already true.
        Assert.NotNull(await audit.SetLuckExclusion(rec.Id, true));
        Assert.NotNull(await audit.SetLuckExclusion(rec.Id, true));
        Assert.True((await audit.Search(charId, source, null, 1, 25)).Rows.Single().ExcludedFromLuck);

        Assert.NotNull(await audit.SetLuckExclusion(rec.Id, false));
        Assert.False((await audit.Search(charId, source, null, 1, 25)).Rows.Single().ExcludedFromLuck);

        // A record that has since been deleted reports nothing to invalidate rather than throwing.
        Assert.Null(await audit.SetLuckExclusion(int.MaxValue, true));
    }
}
