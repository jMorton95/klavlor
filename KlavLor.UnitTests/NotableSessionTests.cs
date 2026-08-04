using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.UnitTests;

// The feed's recent-activity panel exists to show what people actually did, which means it has to
// distinguish a real grind from kills picked up in passing. That judgement is a pure predicate, so
// it's pinned here rather than being discoverable only by staring at a live popover.
public sealed class NotableSessionTests
{
    private const long Interesting = LootFeedGrouping.MinOneOffSessionValue;

    [Fact]
    public void SingleCheapKillAtSourceWithoutClog_IsNotASession()
    {
        // The case the filter exists for: one Hill Giant on the way somewhere.
        Assert.False(LootFeedGrouping.IsNotableSession(
            rolls: 1, gp: 500, clogDrops: 0, sourceHasClogItems: false));
    }

    [Fact]
    public void SingleRaidWithPoorLoot_IsStillASession()
    {
        // A raid nobody would do accidentally. Volume and value both say "ignore me"; the fact that
        // the source has uniques behind it is the only thing that says otherwise, and it wins.
        Assert.True(LootFeedGrouping.IsNotableSession(
            rolls: 1, gp: 1_200, clogDrops: 0, sourceHasClogItems: true));
    }

    [Fact]
    public void GrindWithNoValuableLoot_IsASession()
    {
        // 4,000 Lizardman Shamans that dropped nothing over the feed floor: invisible in the
        // swimlanes, and precisely what this panel is for.
        Assert.True(LootFeedGrouping.IsNotableSession(
            rolls: 4_000, gp: 900, clogDrops: 0, sourceHasClogItems: false));
    }

    [Fact]
    public void SingleValuableKill_IsASession()
    {
        Assert.True(LootFeedGrouping.IsNotableSession(
            rolls: 1, gp: Interesting, clogDrops: 0, sourceHasClogItems: false));
    }

    [Fact]
    public void FirstTimeClogDrop_IsASession()
    {
        // A clog first can come off a cheap item at a source whose drop table we have no rates for,
        // so it has to qualify on its own rather than relying on sourceHasClogItems.
        Assert.True(LootFeedGrouping.IsNotableSession(
            rolls: 1, gp: 12, clogDrops: 1, sourceHasClogItems: false));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(LootFeedGrouping.MinNotableSessionRolls, true)]
    public void RollCountThreshold_IsInclusive(int rolls, bool expected)
    {
        Assert.Equal(expected, LootFeedGrouping.IsNotableSession(
            rolls, gp: 0, clogDrops: 0, sourceHasClogItems: false));
    }

    [Fact]
    public void ValueThreshold_IsInclusiveAndMatchesTheFeedFloor()
    {
        // Deliberately the same 10k the feed uses to decide a drop is worth publishing, so a kill
        // that made the swimlanes can never be filtered out of the activity panel as noise.
        Assert.False(LootFeedGrouping.IsNotableSession(1, Interesting - 1, 0, false));
        Assert.True(LootFeedGrouping.IsNotableSession(1, Interesting, 0, false));
    }
}
