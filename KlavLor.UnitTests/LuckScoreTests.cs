using KlavLor.Application.Features.Loot.Leaderboard;

namespace KlavLor.UnitTests;

// The leaderboard's ranking score has to carry two things that pull in different directions, so the
// properties worth pinning are the ORDERINGS it produces, not the numbers themselves. Every case
// here is a comparison someone could reasonably argue about, which is exactly why it's written down.
public sealed class LuckScoreTests
{
    private static double S(double multiple, double expectedRolls) => LuckScore.For(multiple, expectedRolls);

    [Fact]
    public void AMildStreakOnAVeryRareItem_beatsABiggerStreakOnACommonOne()
    {
        // The requirement the whole formula was built around: 6,250 rolls chasing a 1/5000 is a
        // bigger deal than 2,000 rolls chasing a 1/1000, even though the second is "more dry".
        var rare = S(1.25, 5000);
        var common = S(2.0, 1000);

        Assert.True(rare > common, $"expected {rare:0.#} > {common:0.#}");
        // And by a clear margin, not a hair — a coin-flip ordering would be worse than either rule.
        Assert.InRange(rare / common, 1.2, 1.6);
    }

    [Fact]
    public void TheRarityWeightingItselfGrowsWithRarity()
    {
        // This is what separates the score from a flat square-root weighting: a 1/5000 is weighted
        // harder than a 1/1000, and a 1/10000 harder again.
        var e100 = LuckScore.RarityExponent(100);
        var e1000 = LuckScore.RarityExponent(1000);
        var e5000 = LuckScore.RarityExponent(5000);
        var e10000 = LuckScore.RarityExponent(10000);

        Assert.True(e100 < e1000 && e1000 < e5000 && e5000 < e10000);
        // Gently, though — the whole point is "slightly harder", not a different regime.
        Assert.InRange(e5000 - e1000, 0.01, 0.06);
    }

    [Fact]
    public void ExtremeStreaksStillHoldTheTopOfTheBoard()
    {
        // The failure mode of weighting rarity too hard: a completely ordinary 1x streak on a rare
        // item outranking a genuinely absurd one. 5x is a 0.7% event and 10x is 0.005%; a 1x streak
        // is 37%, i.e. more likely than not.
        Assert.True(S(5.0, 1000) > S(1.0, 10000));
        Assert.True(S(10.0, 100) > S(1.0, 10000));
    }

    [Theory]
    [InlineData(1.0, 2.0)]
    [InlineData(2.0, 5.0)]
    [InlineData(5.0, 10.0)]
    public void ForAFixedItem_dryerAlwaysRanksHigher(double lower, double higher)
    {
        Assert.True(S(higher, 1000) > S(lower, 1000));
    }

    [Theory]
    [InlineData(100, 1000)]
    [InlineData(1000, 5000)]
    [InlineData(5000, 10000)]
    public void ForAFixedMultiple_rarerAlwaysRanksHigher(double commoner, double rarer)
    {
        Assert.True(S(2.0, rarer) > S(2.0, commoner));
    }

    [Fact]
    public void QuotingADepthSourceInDelves_leavesTheDrynessAloneButRaisesItsStanding()
    {
        // Doom is counted in delves rather than runs: an Eye of ayak at 293 runs of depth 8 is 2,344
        // delves against 1,184 expected. Both sides scale by the same factor, so the dryness the user
        // sees is untouched — which is what makes the change safe.
        const double runsObserved = 293, runsExpected = 148, depth = 8;
        var delvesObserved = runsObserved * depth;
        var delvesExpected = runsExpected * depth;

        Assert.Equal(runsObserved / runsExpected, delvesObserved / delvesExpected, precision: 9);

        // What does change is the rarity weight: the same streak is now judged as a ~1,200-roll grind
        // rather than a ~150-roll one, which is the whole point of the exercise.
        var multiple = runsObserved / runsExpected;
        var asRuns = S(multiple, runsExpected);
        var asDelves = S(multiple, delvesExpected);

        Assert.True(asDelves > asRuns * 2,
            $"expected delve scaling to lift the score well clear, got {asRuns:0.#} -> {asDelves:0.#}");
    }

    [Fact]
    public void NonsenseInputsScoreZeroRatherThanThrowing()
    {
        // The refresh service filters these out before scoring, but a NaN leaking into an ORDER BY
        // would corrupt the whole board rather than one row, so the guard is worth having.
        Assert.Equal(0, S(0, 1000));
        Assert.Equal(0, S(-1, 1000));
        Assert.Equal(0, S(2.0, 0));
    }

    [Fact]
    public void TheScoreIsFiniteAcrossTheRealisticRange()
    {
        // Rarest things in OSRS are around 1/10,000-1/20,000; multiples beyond 20x are vanishingly
        // rare. Nothing here should overflow or lose precision in a double.
        foreach (var e in new double[] { 100, 1000, 5000, 20000, 100000 })
        foreach (var m in new[] { 1.01, 2.0, 10.0, 50.0 })
        {
            var score = S(m, e);
            Assert.True(double.IsFinite(score) && score > 0, $"m={m}, e={e} gave {score}");
        }
    }
}
