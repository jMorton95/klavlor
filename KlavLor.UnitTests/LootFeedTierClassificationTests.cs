using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.UnitTests;

// CLAUDE.md: "Feed tiers are per drop, everywhere. Anything that classifies an item into a swimlane
// must use the value of a single receipt, never a running total — LootDropSummary.BestDropValue
// exists for exactly this, so 500 cheap drops summing to millions can't read as a legendary."
//
// That rule spans the live feed cards, the character/source page drop grid and the SSE streams, and
// it is enforced by everything going through ILootFeedService.GetDropTier. These tests pin the
// thresholds and the per-drop rule.
public sealed class LootFeedTierClassificationTests
{
    [Theory]
    // Below the interesting-drop floor: not published at all.
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(9_999, null)]
    // Standard: 10K-100K
    [InlineData(10_000, LootFeedTier.Standard)]
    [InlineData(99_999, LootFeedTier.Standard)]
    // Uncommon: 100K-1M
    [InlineData(100_000, LootFeedTier.Uncommon)]
    [InlineData(999_999, LootFeedTier.Uncommon)]
    // Rare: 1M-10M
    [InlineData(1_000_000, LootFeedTier.Rare)]
    [InlineData(9_999_999, LootFeedTier.Rare)]
    // Epic: 10M-100M
    [InlineData(10_000_000, LootFeedTier.Epic)]
    [InlineData(99_999_999, LootFeedTier.Epic)]
    // Legendary: 100M+
    [InlineData(100_000_000, LootFeedTier.Legendary)]
    [InlineData(long.MaxValue, LootFeedTier.Legendary)]
    public void GetDropTier_bands_a_single_drop_value(long dropValue, LootFeedTier? expected)
    {
        Assert.Equal(expected, ILootFeedService.GetDropTier(dropValue));
    }

    [Fact]
    public void GetDropTier_treats_a_negative_value_as_uninteresting_rather_than_throwing()
    {
        Assert.Null(ILootFeedService.GetDropTier(-1));
    }

    [Fact]
    public void Every_tier_boundary_agrees_with_GetTierRange()
    {
        // The two halves of the contract - the classifier and the range used for the SQL/feed
        // queries - have to describe the same bands or a drop lands in a lane it is then filtered
        // out of.
        foreach (var tier in ILootFeedService.AllTiers)
        {
            var (min, max) = ILootFeedService.GetTierRange(tier);

            Assert.Equal(tier, ILootFeedService.GetDropTier(min));
            if (max is null)
            {
                Assert.Equal(tier, ILootFeedService.GetDropTier(long.MaxValue));
            }
            else
            {
                Assert.Equal(tier, ILootFeedService.GetDropTier(max.Value - 1));
                // The top of a band belongs to the next tier up, never to this one.
                Assert.NotEqual(tier, ILootFeedService.GetDropTier(max.Value));
            }
        }
    }

    // ------------------------------------------------------------------ the per-drop rule

    [Fact]
    public void Five_hundred_cheap_drops_summing_to_millions_do_not_read_as_a_legendary()
    {
        // 500 separate receipts of a 250K item: 125M in total, which as a running total would be
        // Legendary. Each individual receipt is only Uncommon, and that is the only thing that may
        // decide a swimlane.
        const int receipts = 500;
        const int unitValue = 250_000;

        var drops = Enumerable.Range(0, receipts)
            .Select(_ => new LootFeedDrop("Rune platebody", Quantity: 1, Price: unitValue))
            .ToList();

        var classified = ILootFeedService.ClassifyDropsByTier(drops);

        Assert.Equal(LootFeedTier.Uncommon, Assert.Single(classified.Keys));
        Assert.Equal(receipts, classified[LootFeedTier.Uncommon].Count);
        Assert.DoesNotContain(LootFeedTier.Legendary, classified.Keys);
        Assert.DoesNotContain(LootFeedTier.Epic, classified.Keys);
        Assert.DoesNotContain(LootFeedTier.Rare, classified.Keys);

        // ...and for contrast, the running total genuinely WOULD be Legendary. This is the exact
        // arithmetic a per-total classifier would have done.
        var runningTotal = (long)receipts * unitValue;
        Assert.Equal(LootFeedTier.Legendary, ILootFeedService.GetDropTier(runningTotal));
    }

    [Fact]
    public void A_lot_of_sub_floor_drops_never_becomes_an_interesting_one()
    {
        // 5,000 drops worth 9,999 each is ~50M in total but every one of them is below the 10K floor,
        // so nothing at all is published.
        var drops = Enumerable.Range(0, 5_000)
            .Select(_ => new LootFeedDrop("Bones", 1, 9_999))
            .ToList();

        Assert.Empty(ILootFeedService.ClassifyDropsByTier(drops));
    }

    [Fact]
    public void LootDropSummary_exposes_the_biggest_single_receipt_separately_from_the_total()
    {
        // BestDropValue exists precisely so the character/source page drop grid can tier an item
        // without ever looking at TotalValue.
        var summary = new LootDropSummary(
            Name: "Rune platebody",
            TotalQuantity: 500,
            TotalValue: 125_000_000,
            BestDropValue: 250_000);

        Assert.Equal(LootFeedTier.Uncommon, ILootFeedService.GetDropTier(summary.BestDropValue));
        Assert.Equal(LootFeedTier.Legendary, ILootFeedService.GetDropTier(summary.TotalValue));
        Assert.NotEqual(
            ILootFeedService.GetDropTier(summary.TotalValue),
            ILootFeedService.GetDropTier(summary.BestDropValue));
    }

    [Fact]
    public void BestDropValue_defaults_to_zero_so_an_unpopulated_summary_is_untiered_not_legendary()
    {
        // A summary built without the biggest-receipt column must fail closed (no tier), never
        // inherit the running total's tier.
        var summary = new LootDropSummary("Rune platebody", TotalQuantity: 500, TotalValue: 125_000_000);

        Assert.Equal(0L, summary.BestDropValue);
        Assert.Null(ILootFeedService.GetDropTier(summary.BestDropValue));
    }

    // -------------------------------------------------------------- ClassifyDropsByTier

    [Fact]
    public void ClassifyDropsByTier_values_a_stack_by_quantity_times_price()
    {
        // One receipt of 200 items at 60K each is a single 12M drop, so it IS Epic — a stack is one
        // receipt, unlike 200 separate receipts.
        var classified = ILootFeedService.ClassifyDropsByTier(
            [new LootFeedDrop("Dragon bolts", Quantity: 200, Price: 60_000)]);

        Assert.Equal(LootFeedTier.Epic, Assert.Single(classified.Keys));
    }

    [Fact]
    public void ClassifyDropsByTier_does_not_overflow_on_a_large_stack()
    {
        // Quantity and Price are both int; the classifier casts quantity to long before multiplying.
        // Without that cast this would wrap negative and vanish from the feed.
        var classified = ILootFeedService.ClassifyDropsByTier(
            [new LootFeedDrop("Coins", Quantity: 100_000, Price: 50_000)]);

        Assert.Equal(LootFeedTier.Legendary, Assert.Single(classified.Keys));
    }

    [Fact]
    public void One_kill_can_produce_entries_on_several_tiers_at_once()
    {
        var drops = new List<LootFeedDrop>
        {
            new("Bones", 1, 500),                 // below the floor, dropped entirely
            new("Rune sword", 1, 20_000),         // Standard
            new("Dragon chainbody", 1, 400_000),  // Uncommon
            new("Draconic visage", 1, 5_000_000), // Rare
            new("Twisted bow", 1, 1_100_000_000)  // Legendary
        };

        var classified = ILootFeedService.ClassifyDropsByTier(drops);

        Assert.Equal(4, classified.Count);
        Assert.Equal("Rune sword", Assert.Single(classified[LootFeedTier.Standard]).Name);
        Assert.Equal("Dragon chainbody", Assert.Single(classified[LootFeedTier.Uncommon]).Name);
        Assert.Equal("Draconic visage", Assert.Single(classified[LootFeedTier.Rare]).Name);
        Assert.Equal("Twisted bow", Assert.Single(classified[LootFeedTier.Legendary]).Name);
        Assert.False(classified.ContainsKey(LootFeedTier.Epic));
    }

    [Fact]
    public void An_admin_injected_untradeable_is_forced_to_the_top_lane_despite_having_no_value()
    {
        // Infernal Cape / Dizana's Quiver have zero GP value, so they would never tier by price —
        // they are pushed to Legendary so the giga effect can render.
        var classified = ILootFeedService.ClassifyDropsByTier(
            [new LootFeedDrop("Infernal cape", 1, 0, IsSpecial: true)]);

        Assert.Equal(LootFeedTier.Legendary, Assert.Single(classified.Keys));
    }

    [Fact]
    public void ClassifyDropsByTier_preserves_order_within_a_tier_and_returns_no_empty_lanes()
    {
        var drops = new List<LootFeedDrop>
        {
            new("First", 1, 20_000),
            new("Second", 1, 30_000),
            new("Third", 1, 40_000)
        };

        var classified = ILootFeedService.ClassifyDropsByTier(drops);

        Assert.Equal(["First", "Second", "Third"], classified[LootFeedTier.Standard].Select(d => d.Name));
        Assert.All(classified.Values, list => Assert.NotEmpty(list));
        Assert.Empty(ILootFeedService.ClassifyDropsByTier([]));
    }

    [Fact]
    public void MinOneOffSessionValue_mirrors_the_feeds_interesting_drop_floor()
    {
        // A single-kill session below this is hidden from the session list as noise. It is documented
        // as mirroring the feed floor, so if one moves the other must.
        var floor = ILootFeedService.GetTierRange(LootFeedTier.Standard).Min;

        Assert.Equal(LootFeedGrouping.MinOneOffSessionValue, floor);
    }
}
