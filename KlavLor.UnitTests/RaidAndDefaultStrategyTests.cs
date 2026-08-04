using KlavLor.Application.Features.Loot.SourceModels;

namespace KlavLor.UnitTests;

// A raid chest lists each unique as its SHARE of the unique table, not a per-raid probability: a CoX
// prayer scroll shown as 20/69 is ~29% of uniques, not ~1 in 3.45 raids. RaidUniqueShareStrategy
// scales those shares by the average completions per unique a single player sees. Everything with a
// different denominator (tertiary rolls: pets, dust, thread) is already per-completion and passes
// through untouched.
public sealed class RaidAndDefaultStrategyTests
{
    // ------------------------------------------------------------ DefaultSourceLootStrategy

    [Fact]
    public void The_default_strategy_is_keyed_on_the_empty_string()
    {
        var strategy = new DefaultSourceLootStrategy();

        Assert.Equal(string.Empty, strategy.SourceName);
        Assert.True(strategy.IncludeInLeaderboard);
        Assert.False(strategy.OverridesStoredRates);
        Assert.False(strategy.HasDepthModel);
        Assert.Null(strategy.ExpectedCompletionsForRuns("Anything", [5, 6, 7]));
    }

    [Fact]
    public void An_ordinary_claim_is_exactly_one_effective_kill_whatever_dropped()
    {
        var strategy = new DefaultSourceLootStrategy();

        Assert.Equal(1, strategy.EffectiveKills([]));
        Assert.Equal(1, strategy.EffectiveKills([new ClaimDrop("Coins", 1)]));
        Assert.Equal(1, strategy.EffectiveKills(
            [new ClaimDrop("Coins", 1), new ClaimDrop("Twisted bow", 1), new ClaimDrop("Bones", 40)]));
    }

    [Theory]
    // one roll at 1/n costs n kills
    [InlineData(1, 128, 1, 128)]
    [InlineData(1, 5000, 1, 5000)]
    // extra rolls per kill divide the expectation
    [InlineData(1, 512, 4, 128)]
    // a numerator above 1 is a better-than-1/n rate
    [InlineData(3, 300, 1, 100)]
    public void The_default_strategy_treats_the_stored_rate_as_a_flat_per_kill_probability(
        int numerator, int denominator, int rolls, double expected)
    {
        Assert.Equal(expected, new DefaultSourceLootStrategy().ExpectedCompletions(numerator, denominator, rolls), 9);
    }

    [Fact]
    public void An_absent_or_nonsensical_denominator_yields_no_finite_expectation()
    {
        var strategy = new DefaultSourceLootStrategy();

        // double.MaxValue is the sentinel SourceLootService.EffectiveRate turns into "no rate".
        Assert.Equal(double.MaxValue, strategy.ExpectedCompletions(1, 0, 1));
        Assert.Equal(double.MaxValue, strategy.ExpectedCompletions(1, -5, 1));
    }

    [Fact]
    public void A_zero_or_negative_roll_count_or_numerator_is_floored_at_one()
    {
        var strategy = new DefaultSourceLootStrategy();

        // Math.Max(1, ...) on both, so a missing rolls column behaves like one roll rather than
        // producing an infinite expectation.
        Assert.Equal(500, strategy.ExpectedCompletions(1, 500, 0), 9);
        Assert.Equal(500, strategy.ExpectedCompletions(1, 500, -3), 9);
        Assert.Equal(500, strategy.ExpectedCompletions(0, 500, 1), 9);
        Assert.Equal(500, strategy.ExpectedCompletions(-2, 500, 1), 9);
    }

    // ------------------------------------------------------------ RaidUniqueShareStrategy

    // The three raid strategies, restated here independently of their own constructor arguments so a
    // change to a completions-per-unique factor or a share denominator has to be made twice.
    private static readonly (RaidUniqueShareStrategy Strategy, string SourceName,
        double CompletionsPerUnique, int[] TableDenominators)[] Raids =
    [
        (new ChambersOfXericStrategy(), "Chambers of Xeric", 32, [69]),
        (new TombsOfAmascutStrategy(), "Tombs of Amascut", 21, [24]),
        (new TheatreOfBloodStrategy(), "Theatre of Blood", 36, [19, 18])
    ];

    [Fact]
    public void A_raid_claim_is_one_completion_and_the_raid_is_not_depth_modelled()
    {
        foreach (var (strategy, sourceName, _, _) in Raids)
        {
            Assert.Equal(sourceName, strategy.SourceName);
            Assert.Equal(1, strategy.EffectiveKills([new ClaimDrop("Twisted bow", 1), new ClaimDrop("Coins", 500)]));
            // Raids' luck IS computable, unlike Doom's — they just need this normalisation.
            Assert.True(strategy.IncludeInLeaderboard);
            // EffectiveKills of 1 is a roll count, NOT a depth. Confusing the two had the character
            // page announcing "790 delves across 790 runs" for Chambers of Xeric.
            Assert.False(strategy.HasDepthModel, $"{sourceName} must not be depth-modelled");
            Assert.False(strategy.OverridesStoredRates, $"{sourceName} must still trust its stored rates");
            Assert.Null(strategy.ExpectedCompletionsForRuns("Twisted bow", [1, 1, 1]));
        }
    }

    [Fact]
    public void A_unique_table_share_is_scaled_by_the_completions_per_unique()
    {
        foreach (var (strategy, sourceName, completionsPerUnique, denominators) in Raids)
        foreach (var tableDenominator in denominators)
        {
            const int numerator = 3;
            var flatShare = (double)tableDenominator / numerator;

            var actual = strategy.ExpectedCompletions(numerator, tableDenominator, rolls: 1);

            Assert.Equal(flatShare * completionsPerUnique, actual, 9);
            // The whole point: the scaled answer is far larger than the raw share, which is what a
            // hand-rolled call site produced.
            Assert.True(actual > flatShare * 10, $"{sourceName} share {numerator}/{tableDenominator} was not scaled");
        }
    }

    [Fact]
    public void A_tertiary_roll_is_already_per_completion_and_passes_through_unscaled()
    {
        foreach (var (strategy, sourceName, _, denominators) in Raids)
        {
            // Pets and other tertiaries use their own denominator, which is not the unique table's
            // total weight, so they must not be scaled.
            var nonTableDenominator = Enumerable.Range(2, 5000).First(d => !denominators.Contains(d));

            Assert.Equal(nonTableDenominator,
                strategy.ExpectedCompletions(1, nonTableDenominator, rolls: 1), 9);
            _ = sourceName;
        }
    }

    [Fact]
    public void Only_the_table_denominator_triggers_scaling_not_a_nearby_one()
    {
        // The share is identified purely by its denominator matching the table's total weight, so a
        // 68 or a 70 must behave like an ordinary rate. This is the discriminator the whole strategy
        // rests on, and it is deliberately exact rather than approximate.
        var cox = new ChambersOfXericStrategy();

        Assert.Equal(69.0 / 20.0 * 32, cox.ExpectedCompletions(20, 69, 1), 9);
        Assert.Equal(68.0 / 20.0, cox.ExpectedCompletions(20, 68, 1), 9);
        Assert.Equal(70.0 / 20.0, cox.ExpectedCompletions(20, 70, 1), 9);
    }

    [Fact]
    public void Theatre_of_blood_scales_both_its_normal_and_hard_mode_tables()
    {
        var tob = new TheatreOfBloodStrategy();

        Assert.Equal(19.0 / 2 * 36, tob.ExpectedCompletions(2, 19, 1), 9);
        Assert.Equal(18.0 / 2 * 36, tob.ExpectedCompletions(2, 18, 1), 9);
        // 17 is neither table.
        Assert.Equal(17.0 / 2, tob.ExpectedCompletions(2, 17, 1), 9);
    }

    [Fact]
    public void A_raid_with_no_usable_denominator_still_has_no_finite_expectation()
    {
        // The unscaled sentinel must survive the multiplication, not become a finite-looking number.
        Assert.Equal(double.MaxValue, new ChambersOfXericStrategy().ExpectedCompletions(1, 0, 1));
    }
}
