using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.UnitTests;

// The live feed only gives a drop a lucky/dry verdict when it is rare enough for one to mean
// anything. Below 1 in 6, ordinary variance renders as a lurid multiple, and a guaranteed 1/1 drop
// reports its entire kill count as dryness — the production bug where an Atlatl dart from a 300th
// Lunar Chest read as "300x dry".
//
// The threshold is deliberately on EXPECTED ROLLS from SourceLootService rather than on the raw
// stored denominator. That is the whole reason the three raids need no special case: a Chambers of
// Xeric prayer scroll is stored as 20/69, which as a bare fraction looks like 1 in 3.45 and would be
// filtered out, but it is a share of the unique table, not a per-raid chance.
public sealed class FeedLuckRulesTests
{
    private static SourceLootService Service() =>
        new([
            new DefaultSourceLootStrategy(),
            new DoomLootStrategy(),
            new ChambersOfXericStrategy(),
            new TombsOfAmascutStrategy(),
            new TheatreOfBloodStrategy()
        ], new NoRateModifiers());

    [Theory]
    [InlineData(null, false)]   // no usable rate
    [InlineData(1.0, false)]    // guaranteed — the Atlatl dart case
    [InlineData(2.0, false)]
    [InlineData(5.0, false)]
    [InlineData(5.999, false)]
    [InlineData(6.0, true)]     // 1 in 6 exactly is the floor, inclusive
    [InlineData(100.0, true)]
    public void Only_items_rarer_than_one_in_six_get_a_verdict(double? expectedKc, bool expected)
    {
        Assert.Equal(expected, FeedLuckRules.WorthRating(expectedKc));
    }

    [Theory]
    // Every raid unique is stored as its share of the unique table, not as a per-raid chance.
    [InlineData("Chambers of Xeric", "Dexterous prayer scroll", 20, 69)]
    [InlineData("Chambers of Xeric", "Twisted bow", 2, 69)]
    [InlineData("Tombs of Amascut", "Osmumten's fang", 6, 24)]
    [InlineData("Theatre of Blood", "Avernic defender hilt", 8, 19)]
    [InlineData("Theatre of Blood", "Scythe of vitur", 2, 18)]
    public void Every_raid_unique_clears_the_threshold(string source, string item, int numerator, int denominator)
    {
        var rate = Service().EffectiveRate(source, item, numerator, denominator, rolls: 1);

        Assert.NotNull(rate);
        Assert.True(FeedLuckRules.WorthRating(rate!.Value.ExpectedKc),
            $"{item} expected {rate.Value.ExpectedKc:0.#} rolls, which should clear the {FeedLuckRules.MinExpectedRolls} floor");
    }

    [Theory]
    // The common shares are the ones that would have been wrongly filtered had the threshold read
    // the stored fraction: each looks like 1 in 5 or better until the strategy scales it.
    [InlineData("Chambers of Xeric", "Dexterous prayer scroll", 20, 69)]  // bare ~1/3.5
    [InlineData("Tombs of Amascut", "Osmumten's fang", 6, 24)]            // bare 1/4
    [InlineData("Theatre of Blood", "Avernic defender hilt", 8, 19)]      // bare ~1/2.4
    public void A_common_raid_share_would_have_been_filtered_on_its_bare_fraction(
        string source, string item, int numerator, int denominator)
    {
        Assert.False(FeedLuckRules.WorthRating((double)denominator / numerator));

        var rate = Service().EffectiveRate(source, item, numerator, denominator, rolls: 1);
        Assert.True(FeedLuckRules.WorthRating(rate!.Value.ExpectedKc));
    }

    [Fact]
    public void A_guaranteed_drop_is_filtered_even_though_it_has_a_usable_rate()
    {
        // 1/1 from an ordinary source: SourceLootService reports it honestly, the feed declines to
        // rate it. Without this an Atlatl dart on the 300th Lunar Chest claimed 300x dry.
        var rate = Service().EffectiveRate("Lunar Chest", "Atlatl dart", numerator: 1, denominator: 1, rolls: 1);

        Assert.NotNull(rate);
        Assert.Equal(1, rate!.Value.ExpectedKc, 9);
        Assert.False(FeedLuckRules.WorthRating(rate.Value.ExpectedKc));
    }
}
