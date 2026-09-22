using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// An admin can hide ONE drop on ONE kill from the entire site without deleting the kill
// (BlacklistedLootDrop). The case is an item that is not loot at all — RuneLite logging something as
// it was equipped and attributing it to whatever was opened at that moment.
//
// THE MECHANISM IS THE THING BEING PINNED HERE, and it is the reason this feature needed no changes
// at ~150 query sites. A blacklisted drop is left OUT of the derived LootDrops projection and out of
// the LootRecords.TotalValue rolled up from it. Every SQL read site on the site queries those, so
// they all stop seeing the drop by construction. DropsJson keeps it, untouched, which is what makes
// the decision reversible.
//
// So these tests assert against the projection and the total rather than against any one page: if
// those two are right, the drop grid, the loot chart, the source tables, the collection log, the
// profile stats and the global item and source pages are all right together, and a page added next
// year is right without knowing the feature exists.
//
// The fixture's database is shared across the whole collection, so every test scopes itself with its
// own source name and its own item ids.
[Collection("postgres")]
public sealed class DropBlacklistTests(PostgresFixture fx)
{
    private static LootRecordRepository Records(DataContext ctx) =>
        new(ctx, NullLogger<LootRecordRepository>.Instance);

    private static ILootRecordAuditRepository Audit(DataContext ctx) =>
        new LootRecordAuditRepository(
            ctx, Records(ctx), new FakeItemValueCache(), NullLogger<LootRecordAuditRepository>.Instance);

    private static LootSourceDetailRepository SourceDetail(DataContext ctx, params int[] clogIds) =>
        new(ctx, NullLogger<LootSourceDetailRepository>.Instance, new FakeClogCache(clogIds), Fakes.DropReader());

    private static Task<List<LootDropRow>> ProjectedDrops(DataContext ctx, int recordId) =>
        ctx.LootDrops.AsNoTracking().Where(d => d.LootRecordId == recordId).ToListAsync();

    private static Task<long> TotalValue(DataContext ctx, int recordId) =>
        ctx.LootRecords.AsNoTracking().Where(r => r.Id == recordId).Select(r => r.TotalValue).FirstAsync();

    [Fact]
    public async Task A_blacklisted_drop_leaves_the_projection_and_its_gold_leaves_the_total()
    {
        const string source = "DBL1_Chest";
        const string phantom = "DBL1 phantom dossier item";
        const int phantomId = 920_101;
        const string real = "DBL1 real loot";
        const int realId = 920_102;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-one");
        var t = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);

        var rec = Seed.AddKill(ctx, userId, charId, source, t, 1,
            [new(phantom, phantomId, 1, 5_000_000), new(real, realId, 2, 100_000)]);
        await ctx.SaveChangesAsync();

        Assert.Equal(5_200_000, await TotalValue(ctx, rec.Id));

        Assert.NotNull(await Audit(ctx).SetDropBlacklist(rec.Id, phantomId, phantom, blacklisted: true, null));

        // Gone from the projection every read site queries...
        var projected = await ProjectedDrops(ctx, rec.Id);
        Assert.Equal([real], projected.Select(d => d.Name));

        // ...and its 5m gone from the record's gold, which is what the loot chart and every GP
        // aggregate on the site are built from.
        Assert.Equal(200_000, await TotalValue(ctx, rec.Id));

        // But still in the canonical record, untouched. This is the whole basis of reversibility,
        // and the reason the audit panel can still show the decision.
        var json = await ctx.LootRecords.AsNoTracking()
            .Where(r => r.Id == rec.Id).Select(r => r.DropsJson).FirstAsync();
        Assert.Contains(phantom, json);
    }

    [Fact]
    public async Task The_kill_still_counts_as_a_roll()
    {
        // The difference from a delete, and the reason this is not just a slower delete: the kill
        // happened. Only the attribution of one item that fell out of it is disowned.
        const string source = "DBL2_Corrupted Hunllef";
        const string phantom = "DBL2 phantom seed";
        const int phantomId = 920_201;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-two");
        var t = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);

        Seed.AddClogItem(ctx, phantomId, phantom, source);
        Seed.AddKill(ctx, userId, charId, source, t, 1, []);
        var withPhantom = Seed.AddKill(ctx, userId, charId, source, t.AddMinutes(5), 2,
            [new(phantom, phantomId, 1, 0, IsFirstTime: true)]);
        Seed.AddKill(ctx, userId, charId, source, t.AddMinutes(10), 3, []);
        await ctx.SaveChangesAsync();

        var before = await SourceDetail(ctx, phantomId).GetSourceCollection(charId, source);
        Assert.Equal(3, before.Runs.Count);
        Assert.Contains(before.Entries, e => e.ItemName == phantom);

        await Audit(ctx).SetDropBlacklist(withPhantom.Id, phantomId, phantom, blacklisted: true, null);

        var after = await SourceDetail(ctx, phantomId).GetSourceCollection(charId, source);

        // Three rolls still, including the one whose drop was disowned.
        Assert.Equal(3, after.Runs.Count);
        // And the item is no longer an obtained collection-log entry, because there is no longer a
        // drop row for it to be derived from.
        Assert.DoesNotContain(after.Entries, e => e.ItemName == phantom);
    }

    [Fact]
    public async Task Restoring_puts_the_drop_back_exactly_as_it_was()
    {
        const string source = "DBL3_Chest";
        const string phantom = "DBL3 phantom item";
        const int phantomId = 920_301;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-three");
        var t = new DateTimeOffset(2026, 4, 3, 10, 0, 0, TimeSpan.Zero);

        var rec = Seed.AddKill(ctx, userId, charId, source, t, 1,
            [new(phantom, phantomId, 3, 1_000, IsFirstTime: true)]);
        await ctx.SaveChangesAsync();

        var audit = Audit(ctx);
        await audit.SetDropBlacklist(rec.Id, phantomId, phantom, blacklisted: true, null);
        Assert.Empty(await ProjectedDrops(ctx, rec.Id));
        Assert.Equal(0, await TotalValue(ctx, rec.Id));

        await audit.SetDropBlacklist(rec.Id, phantomId, phantom, blacklisted: false, null);

        // Re-derived from the canonical DropsJson, so every field comes back — including the
        // first-time flag, which is not something the blacklist row ever held.
        var restored = Assert.Single(await ProjectedDrops(ctx, rec.Id));
        Assert.Equal(phantom, restored.Name);
        Assert.Equal(3, restored.Quantity);
        Assert.Equal(1_000, restored.Price);
        Assert.True(restored.IsFirstTime);
        Assert.Equal(3_000, await TotalValue(ctx, rec.Id));
    }

    [Fact]
    public async Task Toggling_the_same_state_twice_is_a_no_op_rather_than_a_failure()
    {
        // The UI sends the state it wants rather than "flip", so a double click asks for what is
        // already true. It must not stack a second blacklist row — the unique expression index on
        // (record, item, lower(name)) is the backstop, and a second row would survive the first
        // being lifted and keep the drop hidden with nothing on screen explaining why.
        const string source = "DBL4_Chest";
        const string phantom = "DBL4 phantom item";
        const int phantomId = 920_401;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-four");
        var rec = Seed.AddKill(ctx, userId, charId, source, new DateTimeOffset(2026, 4, 4, 10, 0, 0, TimeSpan.Zero), 1,
            [new(phantom, phantomId, 1, 500)]);
        await ctx.SaveChangesAsync();

        var audit = Audit(ctx);
        Assert.NotNull(await audit.SetDropBlacklist(rec.Id, phantomId, phantom, true, null));
        Assert.NotNull(await audit.SetDropBlacklist(rec.Id, phantomId, phantom, true, null));

        Assert.Equal(1, await ctx.BlacklistedLootDrops.CountAsync(b => b.LootRecordId == rec.Id));

        Assert.NotNull(await audit.SetDropBlacklist(rec.Id, phantomId, phantom, false, null));
        Assert.NotNull(await audit.SetDropBlacklist(rec.Id, phantomId, phantom, false, null));

        Assert.Equal(0, await ctx.BlacklistedLootDrops.CountAsync(b => b.LootRecordId == rec.Id));
        Assert.Single(await ProjectedDrops(ctx, rec.Id));
    }

    [Fact]
    public async Task A_character_wide_rebuild_does_not_resurrect_a_blacklisted_drop()
    {
        // RecomputeFirstTimeFlags runs after every imported batch and after a special-drop injection,
        // and rebuilds the whole character's projection straight from DropsJson — which still holds
        // the blacklisted drop by design. This is the exact shape of the bug ProjectionRebuildTests
        // pins for item-value overrides: a rebuild that "restores the projection to what it should
        // have been" silently undoing an admin decision, long after they watched it work.
        const string source = "DBL5_Chest";
        const string phantom = "DBL5 phantom item";
        const int phantomId = 920_501;
        const string real = "DBL5 real loot";
        const int realId = 920_502;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-five");
        var rec = Seed.AddKill(ctx, userId, charId, source, new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero), 1,
            [new(phantom, phantomId, 1, 7_000), new(real, realId, 1, 3_000)]);
        await ctx.SaveChangesAsync();

        await Audit(ctx).SetDropBlacklist(rec.Id, phantomId, phantom, true, null);
        Assert.Equal(3_000, await TotalValue(ctx, rec.Id));

        await Records(ctx).RecomputeFirstTimeFlags(charId);

        Assert.Equal([real], (await ProjectedDrops(ctx, rec.Id)).Select(d => d.Name));
        Assert.Equal(3_000, await TotalValue(ctx, rec.Id));
    }

    [Fact]
    public async Task A_blacklisted_drop_cannot_hold_an_items_first_time_flag()
    {
        // The "first" badge must sit on the earliest receipt anybody can SEE. Left on a hidden drop
        // it would be on nothing; left off the next one it would be missing from the first visible
        // receipt of the item.
        const string source = "DBL6_Chest";
        const string item = "DBL6 repeated item";
        const int itemId = 920_601;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-six");
        var t = new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero);

        var first = Seed.AddKill(ctx, userId, charId, source, t, 1, [new(item, itemId, 1, 100, IsFirstTime: true)]);
        var second = Seed.AddKill(ctx, userId, charId, source, t.AddHours(1), 2, [new(item, itemId, 1, 100)]);
        await ctx.SaveChangesAsync();

        await Audit(ctx).SetDropBlacklist(first.Id, itemId, item, true, null);
        await Records(ctx).RecomputeFirstTimeFlags(charId);

        // The flag has moved to the earliest receipt that still exists.
        Assert.True((await ProjectedDrops(ctx, second.Id)).Single().IsFirstTime);
        Assert.Empty(await ProjectedDrops(ctx, first.Id));
    }

    [Fact]
    public async Task The_audit_panel_still_shows_a_blacklisted_drop_so_it_can_be_lifted()
    {
        // The one surface that must NOT honour the projection. It reads the canonical DropsJson, so
        // the hidden drop is still listed and marked; reading the projection here would make the
        // decision invisible on the only screen that can reverse it. The search has to find it too,
        // which is why it matches the blacklist table as well as the projection.
        const string source = "DBL7_Chest";
        const string phantom = "DBL7 phantom item";
        const int phantomId = 920_701;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-seven");
        var rec = Seed.AddKill(ctx, userId, charId, source, new DateTimeOffset(2026, 4, 7, 10, 0, 0, TimeSpan.Zero), 1,
            [new(phantom, phantomId, 1, 400)]);
        await ctx.SaveChangesAsync();

        var audit = Audit(ctx);
        await audit.SetDropBlacklist(rec.Id, phantomId, phantom, true, null);

        var bySource = await audit.Search(charId, source, null, 1, 25);
        var drop = Assert.Single(bySource.Rows.Single().Drops);
        Assert.Equal(phantom, drop.Name);
        Assert.True(drop.Blacklisted);

        // And found by item name with no source given at all — the hunt the panel exists for.
        var byItem = await audit.Search(charId, null, "DBL7 phantom", 1, 25);
        Assert.Contains(byItem.Rows, r => r.Id == rec.Id);
    }

    [Fact]
    public async Task Searching_by_item_alone_spans_every_source_the_character_has()
    {
        // The source a mis-attributed drop was filed under is precisely what an admin cannot guess.
        // Requiring one before searching made the commonest hunt impossible.
        const string itemName = "DBL8 crystal armour seed";
        const int itemId = 920_801;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-eight");
        var t = new DateTimeOffset(2026, 4, 8, 10, 0, 0, TimeSpan.Zero);

        Seed.AddKill(ctx, userId, charId, "DBL8_Hunllef", t, 1, [new(itemName, itemId, 1, 0)]);
        Seed.AddKill(ctx, userId, charId, "DBL8_Dossier", t.AddHours(1), 1, [new(itemName, itemId, 1, 0)]);
        Seed.AddKill(ctx, userId, charId, "DBL8_Zulrah", t.AddHours(2), 1, [new("DBL8 unrelated", 920_802, 1, 0)]);
        await ctx.SaveChangesAsync();

        var found = await Audit(ctx).Search(charId, null, "DBL8 crystal armour seed", 1, 25);

        Assert.Equal(2, found.TotalRows);
        Assert.Equal(["DBL8_Dossier", "DBL8_Hunllef"], found.Rows.Select(r => r.SourceName).Order());
    }

    [Fact]
    public async Task Blacklisting_one_drop_leaves_every_other_receipt_of_the_item_alone()
    {
        // Per (record, item), not per item. Hiding a phantom Crystal armour seed on one dossier must
        // not hide the real ones the player actually earned.
        const string source = "DBL9_Hunllef";
        const string item = "DBL9 crystal armour seed";
        const int itemId = 920_901;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-nine");
        var t = new DateTimeOffset(2026, 4, 9, 10, 0, 0, TimeSpan.Zero);

        var phantom = Seed.AddKill(ctx, userId, charId, source, t, 1, [new(item, itemId, 1, 1_000)]);
        var genuine = Seed.AddKill(ctx, userId, charId, source, t.AddHours(1), 2, [new(item, itemId, 1, 1_000)]);
        await ctx.SaveChangesAsync();

        await Audit(ctx).SetDropBlacklist(phantom.Id, itemId, item, true, null);

        Assert.Empty(await ProjectedDrops(ctx, phantom.Id));
        Assert.Single(await ProjectedDrops(ctx, genuine.Id));
        Assert.Equal(1_000, await TotalValue(ctx, genuine.Id));
    }

    [Fact]
    public async Task A_deleted_record_is_logged_and_appears_in_the_modifications_list()
    {
        // Deletion stays irreversible; the log is how it stops being invisible. Without it there was
        // no way, months later, to tell a record someone removed from one that never synced.
        const string source = "DBL10_Nex";

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-ten");
        var rec = Seed.AddKill(ctx, userId, charId, source, new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.Zero), 17,
            [new("DBL10 Torva platelegs", 921_001, 1, 40_000_000), new("DBL10 Nihil dust", 921_002, 54, 1_000)]);
        await ctx.SaveChangesAsync();
        var recordId = rec.Id;

        var audit = Audit(ctx);
        Assert.NotNull(await audit.Delete(recordId, "logged as a kill that never happened"));

        Assert.False(await ctx.LootRecords.AnyAsync(r => r.Id == recordId));

        var logged = Assert.Single(
            await ctx.DeletedLootRecordLogs.AsNoTracking().Where(d => d.LootRecordId == recordId).ToListAsync());
        Assert.Equal(source, logged.SourceName);
        Assert.Equal(17, logged.KillCount);
        Assert.Equal(40_054_000, logged.TotalValue);
        Assert.Contains("DBL10 Torva platelegs", logged.DropsSummary);
        Assert.Contains("DBL10 Nihil dust x54", logged.DropsSummary);
        Assert.Equal("logged as a kill that never happened", logged.Reason);

        var mods = await audit.GetModifications(100);
        var entry = Assert.Single(mods, m => m.RecordId == recordId && m.Kind == AuditModificationKind.Deleted);
        Assert.False(entry.Restorable);
    }

    [Fact]
    public async Task The_modifications_list_carries_all_three_kinds_and_says_which_can_be_undone()
    {
        const string source = "DBL11_Chest";
        const string phantom = "DBL11 phantom item";
        const int phantomId = 921_101;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-eleven");
        var t = new DateTimeOffset(2026, 4, 11, 10, 0, 0, TimeSpan.Zero);

        var hidden = Seed.AddKill(ctx, userId, charId, source, t, 1, [new(phantom, phantomId, 1, 100)]);
        var excluded = Seed.AddKill(ctx, userId, charId, source, t.AddHours(1), 2, [new("DBL11 other", 921_102, 1, 100)]);
        var doomed = Seed.AddKill(ctx, userId, charId, source, t.AddHours(2), 3, [new("DBL11 gone", 921_103, 1, 100)]);
        await ctx.SaveChangesAsync();
        int hiddenId = hidden.Id, excludedId = excluded.Id, doomedId = doomed.Id;

        var audit = Audit(ctx);
        await audit.SetDropBlacklist(hiddenId, phantomId, phantom, true, "equipped, not looted");
        await audit.SetLuckExclusion(excludedId, true);
        await audit.Delete(doomedId, null);

        var mods = await audit.GetModifications(100);

        var blacklistRow = Assert.Single(mods, m => m.RecordId == hiddenId);
        Assert.Equal(AuditModificationKind.BlacklistedDrop, blacklistRow.Kind);
        // Detail names the ITEM, which is the unit of this decision — and is what the undo button
        // sends back to identify the drop.
        Assert.Equal(phantom, blacklistRow.Detail);
        Assert.Equal(phantomId, blacklistRow.ItemId);
        Assert.Equal("equipped, not looted", blacklistRow.Reason);
        Assert.True(blacklistRow.Restorable);

        var exclusionRow = Assert.Single(mods, m => m.RecordId == excludedId);
        Assert.Equal(AuditModificationKind.LuckExcluded, exclusionRow.Kind);
        Assert.True(exclusionRow.Restorable);

        var deletionRow = Assert.Single(mods, m => m.RecordId == doomedId);
        Assert.Equal(AuditModificationKind.Deleted, deletionRow.Kind);
        Assert.False(deletionRow.Restorable);
    }

    [Fact]
    public async Task Lifting_a_blacklist_from_the_modifications_list_takes_it_off_the_list()
    {
        const string source = "DBL12_Chest";
        const string phantom = "DBL12 phantom item";
        const int phantomId = 921_201;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-twelve");
        var rec = Seed.AddKill(ctx, userId, charId, source, new DateTimeOffset(2026, 4, 12, 10, 0, 0, TimeSpan.Zero), 1,
            [new(phantom, phantomId, 1, 100)]);
        await ctx.SaveChangesAsync();
        var recordId = rec.Id;

        var audit = Audit(ctx);
        await audit.SetDropBlacklist(recordId, phantomId, phantom, true, null);
        Assert.Contains(await audit.GetModifications(100), m => m.RecordId == recordId);

        await audit.SetDropBlacklist(recordId, phantomId, phantom, false, null);
        Assert.DoesNotContain(await audit.GetModifications(100), m => m.RecordId == recordId);
    }

    [Fact]
    public async Task Deleting_a_record_takes_its_blacklist_rows_with_it()
    {
        // The cascade matters for the modifications list: a blacklist row pointing at a record that
        // no longer exists would render as a change to a kill nobody can find.
        const string source = "DBL13_Chest";
        const string phantom = "DBL13 phantom item";
        const int phantomId = 921_301;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-thirteen");
        var rec = Seed.AddKill(ctx, userId, charId, source, new DateTimeOffset(2026, 4, 13, 10, 0, 0, TimeSpan.Zero), 1,
            [new(phantom, phantomId, 1, 100)]);
        await ctx.SaveChangesAsync();
        var recordId = rec.Id;

        var audit = Audit(ctx);
        await audit.SetDropBlacklist(recordId, phantomId, phantom, true, null);
        await audit.Delete(recordId, null);

        Assert.False(await ctx.BlacklistedLootDrops.AnyAsync(b => b.LootRecordId == recordId));
    }

    [Fact]
    public async Task The_cache_primes_from_what_was_written()
    {
        // Startup and every write re-prime IDropBlacklistCache from this. If the shapes ever drift,
        // the stored projection and the DropsJson readers would disagree — the drop gone from the
        // drop grid but still on the feed card.
        const string source = "DBL14_Chest";
        const string phantom = "DBL14 phantom item";
        const int phantomId = 921_401;

        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dbl-fourteen");
        var rec = Seed.AddKill(ctx, userId, charId, source, new DateTimeOffset(2026, 4, 14, 10, 0, 0, TimeSpan.Zero), 1,
            [new(phantom, phantomId, 1, 100)]);
        await ctx.SaveChangesAsync();

        var audit = Audit(ctx);
        await audit.SetDropBlacklist(rec.Id, phantomId, phantom, true, null);

        var cache = new KlavLor.Infrastructure.Services.DropBlacklistCache();
        cache.Replace(await audit.GetAllBlacklistedDrops());

        Assert.True(cache.IsBlacklisted(rec.Id, phantomId, phantom));
        Assert.False(cache.IsBlacklisted(rec.Id, phantomId, "something else"));
    }
}
