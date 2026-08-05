using KlavLor.Infrastructure.Services;

namespace KlavLor.IntegrationTests;

// Board entry is now one rule for both obtained and still-chasing items: anything past its expected
// roll count qualifies, provided the item is rare enough to be worth a slot. This replaced a split of
// 1.75x for items you own and 1.0x for ones you don't, plus a "rare grind" floor that lifted rare
// items by overwriting their multiple with denominator/1000 — a synthetic value the display then had
// to undo. LuckScore ranks by rarity directly, so none of that is needed.
public sealed class DryStreakEntryRulesTests
{
    [Fact]
    public void A_one_in_hundred_item_qualifies_just_past_its_rate()
    {
        // The worked example: 1/100 item, 101 rolls.
        var multiple = LuckLeaderboardRefreshService.BoardMultiple(observed: 101, expected: 100);

        Assert.NotNull(multiple);
        Assert.Equal(1.01, multiple!.Value, precision: 2);
    }

    [Fact]
    public void Exactly_on_rate_or_better_is_not_a_dry_streak()
    {
        Assert.Null(LuckLeaderboardRefreshService.BoardMultiple(99, 100));
        Assert.Null(LuckLeaderboardRefreshService.BoardMultiple(100, 100));
    }

    [Fact]
    public void An_obtained_item_no_longer_needs_to_clear_two_times()
    {
        // The case that vanished when the Doom delve/run scale error was fixed: correcting the maths
        // dropped a real streak to ~1.8x, which the old obtained-only bar excluded. One bar now, so
        // owning the item is no longer held to a stricter standard than still chasing it.
        Assert.NotNull(LuckLeaderboardRefreshService.BoardMultiple(observed: 267, expected: 148));
        Assert.NotNull(LuckLeaderboardRefreshService.BoardMultiple(observed: 160, expected: 148));
    }

    [Fact]
    public void The_multiple_is_always_the_honest_ratio()
    {
        // A 1/2000 item received at 2,100 rolls is 1.05x dry and reports exactly that. The old rare
        // grind floor ranked it as 1.99x, which is why the results view had to recompute the real
        // ratio for display rather than trust the stored value.
        var multiple = LuckLeaderboardRefreshService.BoardMultiple(observed: 2100, expected: 2000);

        Assert.NotNull(multiple);
        Assert.Equal(1.05, multiple!.Value, precision: 2);
    }
}
