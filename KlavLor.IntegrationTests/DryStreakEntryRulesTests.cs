using KlavLor.Infrastructure.Services;

namespace KlavLor.IntegrationTests;

// The dry board used to hide every not-yet-obtained item until the player was 2x past its expected
// kill count, so a mild but real streak was invisible. Items still being chased now join as soon as
// the player has put in enough kills to have expected the drop once. Items already obtained keep
// the 2x bar — a drop that came in slightly late isn't worth a board slot.
public sealed class DryStreakEntryRulesTests
{
    private const double MinObtained = 2.0;
    private const double MinMissing = 1.0;

    [Fact]
    public void A_missing_one_in_hundred_item_shows_at_one_times_dry_just_past_the_rate()
    {
        // The worked example: 1/100 item, 101 kills, never received.
        var multiple = LuckLeaderboardRefreshService.DryMultiple(observed: 101, expected: 100, rarityDenominator: 100, MinMissing);

        Assert.NotNull(multiple);
        Assert.Equal(1.01, multiple!.Value, 6);
        // Tier is floor(multiple), so it lands on the board as a 1x dry streak.
        Assert.Equal(1, (int)Math.Floor(multiple.Value));
    }

    [Fact]
    public void A_missing_item_short_of_the_rate_still_does_not_qualify()
    {
        // 99 kills on a 1/100 item is not yet a dry streak at all.
        Assert.Null(LuckLeaderboardRefreshService.DryMultiple(99, 100, 100, MinMissing));
        // Exactly on the rate is "on rate", not dry.
        Assert.Null(LuckLeaderboardRefreshService.DryMultiple(100, 100, 100, MinMissing));
    }

    [Fact]
    public void The_same_mild_streak_is_rejected_for_an_item_already_obtained()
    {
        // Obtained items keep the 2x bar, so 101 kills on a 1/100 drop earns nothing...
        Assert.Null(LuckLeaderboardRefreshService.DryMultiple(101, 100, 100, MinObtained));
        // ...but a genuinely late one still qualifies.
        Assert.NotNull(LuckLeaderboardRefreshService.DryMultiple(250, 100, 100, MinObtained));
    }

    [Fact]
    public void Rare_grind_ranking_is_unaffected_by_the_lower_missing_bar()
    {
        // A 1/3000 item past its own rate in kills is still ranked just under tier 3 by rarity,
        // regardless of which minimum applied.
        var missing = LuckLeaderboardRefreshService.DryMultiple(3100, 3000, 3000, MinMissing);
        var obtained = LuckLeaderboardRefreshService.DryMultiple(3100, 3000, 3000, MinObtained);

        Assert.Equal(2.99, missing!.Value, 6);
        Assert.Equal(2.99, obtained!.Value, 6);
    }
}
