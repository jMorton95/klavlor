using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Features.Loot.Superiors;

namespace KlavLor.UnitTests;

// The unique-table weighting the Superiors page is built on.
//
// It matters because every superior rolls the SAME table, so kills of all 38 accumulate toward one
// prize - and are therefore only comparable once weighted. The page showed raw counts for months,
// which silently equated a Colossal Hydra with a Crushing hand worth about an eighth of it.
public sealed class SuperiorUniqueChanceTests
{
    private static SourceLootService Service() =>
        new([new DefaultSourceLootStrategy()], new NoRateModifiers());

    [Theory]
    // The two ends of the registry, against the wiki's own published figures.
    [InlineData(5, 171)]
    [InlineData(95, 20)]
    public void The_chance_matches_the_published_rate_at_each_end_of_the_list(int level, int expected)
    {
        var chance = Service().SuperiorUniqueChance(level);

        Assert.Equal(expected, Math.Round(1 / chance));
    }

    [Fact]
    public void A_colossal_hydra_is_worth_about_eight_and_a_half_crushing_hands()
    {
        // The single fact the page exists to convey, and the reason it is ordered hardest-first.
        var service = Service();

        var ratio = service.SuperiorUniqueChance(95) / service.SuperiorUniqueChance(5);

        Assert.InRange(ratio, 8.4, 8.7);
    }

    [Fact]
    public void The_chance_rises_with_every_level_in_the_registry()
    {
        // Monotonic, which is what makes "hardest first" the same ordering as "most valuable first".
        // If it were not, the page's default sort would be quietly misleading.
        var service = Service();
        var levels = SuperiorSlayerMonsters.All.Select(m => m.SlayerLevel).Distinct().OrderBy(l => l).ToList();

        var chances = levels.Select(service.SuperiorUniqueChance).ToList();

        Assert.Equal(chances.OrderBy(c => c), chances);
        Assert.All(chances, c => Assert.True(c > 0, "every registry level must have a usable chance"));
    }

    [Fact]
    public void An_unusable_level_is_zero_rather_than_negative()
    {
        // The denominator collapses somewhere above level 103. The game cannot reach it and the
        // registry does not contain it, but a typo in a future entry must not become a negative
        // probability that then propagates into a character's expected-uniques total.
        var service = Service();

        Assert.Equal(0, service.SuperiorUniqueChance(0));
        Assert.Equal(0, service.SuperiorUniqueChance(-10));
        Assert.Equal(0, service.SuperiorUniqueChance(200));
    }

    [Fact]
    public void Weighting_can_rank_two_players_differently_from_their_kill_counts()
    {
        // THE PROPERTY THE PAGE IS FOR. Raw totals put the grinder of common superiors ahead; the
        // weighting puts whoever is actually closer to the prize ahead. A page showing only counts
        // cannot distinguish these two players at all.
        var service = Service();

        // 1,000 Crushing hands (level 5) against 200 Colossal Hydra (level 95).
        var grinder = 1000 * service.SuperiorUniqueChance(5);
        var specialist = 200 * service.SuperiorUniqueChance(95);

        Assert.True(1000 > 200, "the grinder has five times the kills");
        Assert.True(specialist > grinder, "but the specialist has more table rolls");
    }
}
