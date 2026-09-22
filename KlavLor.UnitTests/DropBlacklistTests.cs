using KlavLor.Application.Features.Loot;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Services;

namespace KlavLor.UnitTests;

// The admin drop blacklist, at the seam every DropsJson reader goes through.
//
// The blacklist hides a drop by leaving it out of the DERIVED projection, which is what makes every
// SQL read site on the site stop seeing it with no query changed. The handful of sites that read
// the canonical DropsJson instead cannot benefit from that, and go through EffectiveDropReader — so
// these tests are the other half of the guarantee. A regression here is invisible in the obvious
// way: the drop simply keeps appearing on the live feed and the session list while the database and
// every other page agree it is gone, which reads as "the page is stale" rather than as a bug.
public sealed class DropBlacklistTests
{
    private const int Record = 4242;
    private const int OtherRecord = 5353;
    private const int SeedId = 4207;
    private const int BonesId = 526;

    // The REAL cache, not a stand-in: matching is case-insensitive on one half of a composite key,
    // which is exactly the kind of rule a test double quietly gets right for the wrong reason.
    private static DropBlacklistCache Blacklist(params BlacklistedDropValue[] entries)
    {
        var cache = new DropBlacklistCache();
        cache.Replace(entries);
        return cache;
    }

    private sealed class NoOverrides : IItemValueOverrideCache
    {
        public bool HasAny => false;
        public int GetPrice(int itemId, int rawPrice) => rawPrice;
        public void Replace(IEnumerable<ItemValueOverrideValue> overrides) { }
    }

    private sealed class FixedOverride(int itemId, int value) : IItemValueOverrideCache
    {
        public bool HasAny => true;
        public int GetPrice(int id, int rawPrice) => id == itemId ? value : rawPrice;
        public void Replace(IEnumerable<ItemValueOverrideValue> overrides) { }
    }

    private static string Json(params LootDrop[] drops) =>
        System.Text.Json.JsonSerializer.Serialize(drops.ToList());

    [Fact]
    public void A_blacklisted_drop_is_removed_and_its_siblings_survive()
    {
        var reader = new EffectiveDropReader(
            new NoOverrides(), Blacklist(new BlacklistedDropValue(Record, SeedId, "Crystal armour seed")));

        var drops = reader.Read(Record, Json(
            new LootDrop("Crystal armour seed", SeedId, 1, 0),
            new LootDrop("Bones", BonesId, 3, 100)));

        // The whole point of the per-drop unit: the kill keeps everything else it carried.
        Assert.Equal(["Bones"], drops.Select(d => d.Name));
    }

    [Fact]
    public void The_blacklist_is_per_record_not_per_item()
    {
        // Blacklisting an item on ONE kill must not hide every other receipt of it. That would be a
        // different feature (and the wrong one): an item is not banned, one logged drop is disowned.
        var reader = new EffectiveDropReader(
            new NoOverrides(), Blacklist(new BlacklistedDropValue(Record, SeedId, "Crystal armour seed")));

        var elsewhere = reader.Read(OtherRecord, Json(new LootDrop("Crystal armour seed", SeedId, 1, 0)));

        Assert.Single(elsewhere);
    }

    [Fact]
    public void Item_names_match_case_insensitively()
    {
        // Three vocabularies produce an item name and they disagree on case: the wiki's drop rows,
        // RuneLite's drop names, and the collection log ("Scythe of vitur (uncharged)"). A
        // case-sensitive match would store a decision nothing could ever find to apply.
        var reader = new EffectiveDropReader(
            new NoOverrides(), Blacklist(new BlacklistedDropValue(Record, SeedId, "CRYSTAL ARMOUR SEED")));

        Assert.Empty(reader.Read(Record, Json(new LootDrop("Crystal armour seed", SeedId, 1, 0))));
    }

    [Fact]
    public void Two_zero_id_drops_on_one_kill_stay_individually_blacklistable()
    {
        // An untradeable can be logged with no usable id at all, and COALESCE turns that into 0. If
        // the key were the id alone, blacklisting one such drop would take every other one on the
        // same kill with it — which is why the name is part of the key.
        var reader = new EffectiveDropReader(
            new NoOverrides(), Blacklist(new BlacklistedDropValue(Record, 0, "Phantom item")));

        var drops = reader.Read(Record, Json(
            new LootDrop("Phantom item", 0, 1, 0),
            new LootDrop("Real untradeable", 0, 1, 0)));

        Assert.Equal(["Real untradeable"], drops.Select(d => d.Name));
    }

    [Fact]
    public void Prices_and_the_blacklist_are_applied_together()
    {
        // The reader exists so a call site cannot honour one rule and forget the other. An override
        // on a surviving drop must still be applied when something else on the kill was removed.
        var reader = new EffectiveDropReader(
            new FixedOverride(BonesId, 10_000_000),
            Blacklist(new BlacklistedDropValue(Record, SeedId, "Crystal armour seed")));

        var drops = reader.Read(Record, Json(
            new LootDrop("Crystal armour seed", SeedId, 1, 0),
            new LootDrop("Bones", BonesId, 1, 0)));

        var bones = Assert.Single(drops);
        Assert.Equal(10_000_000, bones.Price);
    }

    [Fact]
    public void Nothing_blacklisted_returns_the_list_untouched()
    {
        // Every kill list, feed card and session on the site goes through this. With an empty
        // blacklist — the overwhelmingly common case — it must not walk or copy the list.
        var reader = new EffectiveDropReader(new NoOverrides(), Blacklist());
        var drops = new List<LootDrop> { new("Bones", BonesId, 1, 100) };

        Assert.Same(drops, reader.Apply(Record, drops));
    }

    [Fact]
    public void An_unsaved_record_has_nothing_blacklisted()
    {
        // The ingest path reads drops for a record that may not have an id yet. A drop on a record
        // that does not exist cannot have been blacklisted, and record 0 must not collide with one
        // that has been.
        var reader = new EffectiveDropReader(
            new NoOverrides(), Blacklist(new BlacklistedDropValue(Record, SeedId, "Crystal armour seed")));

        Assert.Single(reader.Read(0, Json(new LootDrop("Crystal armour seed", SeedId, 1, 0))));
    }

    [Fact]
    public void Malformed_or_missing_json_yields_no_drops_rather_than_throwing()
    {
        var reader = new EffectiveDropReader(new NoOverrides(), Blacklist());

        Assert.Empty(reader.Read(Record, null));
        Assert.Empty(reader.Read(Record, ""));
        Assert.Empty(reader.Read(Record, "not json"));
    }

    [Fact]
    public void Replace_swaps_the_whole_snapshot()
    {
        // Writes are rare and reads are hot, so the cache swaps an immutable map rather than
        // mutating one. A lifted blacklist has to actually disappear on the next Replace.
        var cache = new DropBlacklistCache();
        cache.Replace([new BlacklistedDropValue(Record, SeedId, "Crystal armour seed")]);
        Assert.True(cache.IsBlacklisted(Record, SeedId, "Crystal armour seed"));

        cache.Replace([]);
        Assert.False(cache.HasAny);
        Assert.False(cache.IsBlacklisted(Record, SeedId, "Crystal armour seed"));
    }
}
