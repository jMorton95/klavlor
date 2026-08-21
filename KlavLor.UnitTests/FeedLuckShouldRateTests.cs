using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.UnitTests;

// WHICH feed drops get a lucky/dry line at all. Four independent reasons to stay silent, each of
// which has been a visible wrong number on a card at some point:
//
//   - not a collection-log item: a line on every snapdragon seed buries the interesting ones
//   - not the first receipt: the same item four times over is four numbers about one clog slot, and
//     only the first has an honest answer to "how long did this take"
//   - admin-excluded at the record level: a receipt we cannot rate honestly
//   - too common, or no rate at all (see FeedLuckRulesTests for that threshold)
//
// The policy lives in the Application layer precisely so it can be pinned here: LuckLabel is razor
// markup, which no test in this solution can reach.
public sealed class FeedLuckShouldRateTests
{
    // A drop that qualifies on every count, so each test can spoil exactly one thing.
    private static LootFeedDrop Rated(
        bool isCollectionLogItem = true,
        bool isFirstTime = true,
        bool excludedFromLuck = false,
        double? expectedKc = 500,
        string? rarity = "1/500") =>
        new("Twisted bow", 1, 1_000_000_000,
            IsFirstTime: isFirstTime,
            IsCollectionLogItem: isCollectionLogItem,
            IsSpecial: false,
            ExpectedKc: expectedKc,
            EffectiveRarity: rarity,
            KillCount: 300,
            ExcludedFromLuck: excludedFromLuck);

    [Fact]
    public void A_rare_first_receipt_of_a_collection_log_item_is_rated()
    {
        Assert.True(FeedLuckRules.ShouldRate(Rated()));
    }

    [Fact]
    public void A_repeat_receipt_is_never_rated()
    {
        // The change this test exists for: every receipt after the first shows its value and its
        // tier but makes no claim about luck.
        Assert.False(FeedLuckRules.ShouldRate(Rated(isFirstTime: false)));
    }

    [Fact]
    public void An_item_outside_the_collection_log_is_never_rated()
    {
        Assert.False(FeedLuckRules.ShouldRate(Rated(isCollectionLogItem: false)));
    }

    [Fact]
    public void An_admin_excluded_receipt_is_never_rated()
    {
        Assert.False(FeedLuckRules.ShouldRate(Rated(excludedFromLuck: true)));
    }

    [Theory]
    [InlineData(null)]      // no rate held for this item at this source
    [InlineData(0.0)]       // nonsensical rate
    [InlineData(1.0)]       // guaranteed - the Atlatl dart case
    [InlineData(5.999)]     // just inside the "too common to judge" band
    public void A_drop_with_no_usable_or_no_interesting_rate_is_not_rated(double? expectedKc)
    {
        Assert.False(FeedLuckRules.ShouldRate(Rated(expectedKc: expectedKc)));
    }

    [Fact]
    public void A_missing_rarity_label_is_not_rated_even_with_an_expectation()
    {
        // The card prints the rarity string, so an expectation without one would render a line with
        // a hole in it.
        Assert.False(FeedLuckRules.ShouldRate(Rated(rarity: null)));
        Assert.False(FeedLuckRules.ShouldRate(Rated(rarity: "")));
    }

    [Fact]
    public void Every_reason_to_stay_silent_is_independent()
    {
        // Belt and braces: no combination of two disqualifiers accidentally cancels out.
        Assert.False(FeedLuckRules.ShouldRate(Rated(isFirstTime: false, excludedFromLuck: true)));
        Assert.False(FeedLuckRules.ShouldRate(Rated(isCollectionLogItem: false, isFirstTime: false)));
        Assert.False(FeedLuckRules.ShouldRate(Rated(isFirstTime: false, expectedKc: 1.0)));
    }
}
