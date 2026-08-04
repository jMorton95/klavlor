using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.UnitTests;

// The feed's recent-activity panel shows one row per source per character, covering everything they
// did there over the whole window. IsNotableActivity decides which of those rows earn a slot, and
// it's a pure predicate, so it's pinned here rather than being discoverable only by staring at a
// live popover.
public sealed class NotableSessionTests
{
    private const int MinRolls = LootFeedGrouping.MinNotableRolls;
    private const long MinValue = LootFeedGrouping.MinOneOffSessionValue;

    [Fact]
    public void BothBarsMustClear_notEither()
    {
        // The whole design: volume without value is a walk through a slayer task, and value without
        // volume is a single lucky kill the swimlanes already show.
        Assert.False(LootFeedGrouping.IsNotableActivity(rolls: 4_000, gp: MinValue - 1));
        Assert.False(LootFeedGrouping.IsNotableActivity(rolls: MinRolls - 1, gp: 500_000_000));
        Assert.True(LootFeedGrouping.IsNotableActivity(rolls: MinRolls, gp: MinValue));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(MinRolls - 1, false)]
    [InlineData(MinRolls, true)]
    [InlineData(MinRolls + 1, true)]
    public void RollThreshold_isInclusive(int rolls, bool expected)
    {
        Assert.Equal(expected, LootFeedGrouping.IsNotableActivity(rolls, gp: MinValue));
    }

    [Fact]
    public void ValueThreshold_isInclusiveAndMatchesTheFeedFloor()
    {
        // Deliberately the same 10k the feed uses to decide a drop is worth publishing, so the two
        // surfaces agree on what counts as loot at all.
        Assert.False(LootFeedGrouping.IsNotableActivity(MinRolls, MinValue - 1));
        Assert.True(LootFeedGrouping.IsNotableActivity(MinRolls, MinValue));
    }

    [Fact]
    public void AGrindWorthNothing_isHidden()
    {
        // 4,000 Lizardman Shamans that dropped nothing over the floor. Volume alone no longer earns
        // a row — this is the case the tightened filter deliberately removes.
        Assert.False(LootFeedGrouping.IsNotableActivity(rolls: 4_000, gp: 900));
    }

    [Fact]
    public void ARealGrindThatPaidOut_isKept()
    {
        Assert.True(LootFeedGrouping.IsNotableActivity(rolls: 900, gp: 2_000_000));
    }

    [Fact]
    public void ASingleRaid_isHidden()
    {
        // Worth stating outright, because an earlier version kept this on the grounds that the source
        // has uniques behind it. Under the current rule one or two visits in two days does not earn a
        // row however rare the drop table is.
        Assert.False(LootFeedGrouping.IsNotableActivity(rolls: 1, gp: 1_200));
    }
}
