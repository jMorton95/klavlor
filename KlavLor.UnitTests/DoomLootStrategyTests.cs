using KlavLor.Application.Features.Loot.SourceModels;

namespace KlavLor.UnitTests;

// DoomLootStrategy is pure probability maths over a hard-coded per-level rate table, and it is the
// most consequential code in the repository with no test around it. These tests pin the three
// public functions the directive names — ProbabilityOverRun, ExpectedCompletionsForRuns and
// EstimateDepth — including the two properties the file's own comments only *claim*.
public sealed class DoomLootStrategyTests
{
    private const string Cloth = "Mokhaiotl cloth";
    private const string Eye = "Eye of ayak";
    private const string Treads = "Avernic treads";
    private const string Pet = "Dom";
    private const string NotADoomItem = "Twisted bow";

    // Straight from Module:Doom_of_Mokhaiotl_loot, restated here independently of the strategy's
    // own private arrays so a table edit has to be made deliberately in two places.
    private static readonly (string Item, int FirstLevel, int[] Denominators) ClothTable =
        (Cloth, 2, [2500, 2000, 1350, 810, 765, 720, 630, 540]);
    private static readonly (string Item, int FirstLevel, int[] Denominators) EyeTable =
        (Eye, 3, [2000, 1350, 810, 765, 720, 630, 540]);
    private static readonly (string Item, int FirstLevel, int[] Denominators) TreadsTable =
        (Treads, 4, [1350, 810, 765, 720, 630, 540]);
    private static readonly (string Item, int FirstLevel, int[] Denominators) PetTable =
        (Pet, 6, [1000, 750, 500, 250]);

    private static DoomLootStrategy Doom() => new();

    // ---------------------------------------------------------------- ProbabilityOverRun

    [Fact]
    public void ProbabilityOverRun_is_zero_for_an_item_this_strategy_does_not_model()
    {
        var doom = Doom();

        // Not a Doom unique at all.
        Assert.Equal(0.0, doom.ProbabilityOverRun(NotADoomItem, 9));
        Assert.Equal(0.0, doom.ProbabilityOverRun("", 9));
        // Guaranteed accumulating drops are deliberately unmodelled: their stored per-level rarity
        // is meaningless as a per-run chance.
        Assert.Equal(0.0, doom.ProbabilityOverRun("Demon tears", 9));
        Assert.Equal(0.0, doom.ProbabilityOverRun("Mokhaiotl waystone", 9));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ProbabilityOverRun_is_zero_for_a_depth_below_one(int depth)
    {
        Assert.Equal(0.0, Doom().ProbabilityOverRun(Cloth, depth));
    }

    [Fact]
    public void ProbabilityOverRun_is_zero_at_levels_before_an_item_becomes_eligible()
    {
        var doom = Doom();

        // Treads can't roll before level 4, the eye before 3, the cloth before 2, the pet before 6.
        foreach (var (item, firstLevel, _) in new[] { ClothTable, EyeTable, TreadsTable, PetTable })
        {
            for (var depth = 1; depth < firstLevel; depth++)
                Assert.Equal(0.0, doom.ProbabilityOverRun(item, depth));

            Assert.True(doom.ProbabilityOverRun(item, firstLevel) > 0,
                $"{item} must be able to drop at depth {firstLevel}");
        }
    }

    [Fact]
    public void ProbabilityOverRun_treats_each_cleared_level_as_an_independent_trial()
    {
        var doom = Doom();

        // One eligible level only: the probability is exactly that level's rate.
        Assert.Equal(1.0 / 2500, doom.ProbabilityOverRun(Cloth, 2), 12);
        Assert.Equal(1.0 / 1350, doom.ProbabilityOverRun(Treads, 4), 12);
        Assert.Equal(1.0 / 1000, doom.ProbabilityOverRun(Pet, 6), 12);

        // Several eligible levels: 1 - the product of each level's miss chance.
        foreach (var (item, firstLevel, denominators) in new[] { ClothTable, EyeTable, TreadsTable, PetTable })
        {
            for (var depth = firstLevel; depth <= 9; depth++)
            {
                var used = denominators.Take(depth - firstLevel + 1);
                var pNone = used.Aggregate(1.0, (acc, den) => acc * (1.0 - 1.0 / den));
                Assert.Equal(1.0 - pNone, doom.ProbabilityOverRun(item, depth), 12);
            }
        }
    }

    [Fact]
    public void ProbabilityOverRun_clamps_the_rate_index_at_level_nine_for_deeper_delves()
    {
        var doom = Doom();

        const int deepestTabulated = 9;
        const double level9Denominator = 540; // treads / cloth / eye all bottom out at 1/540

        var pNoneAtNine = 1.0 - doom.ProbabilityOverRun(Treads, deepestTabulated);

        // Every level past 9 must reuse the level-9 rate, so the miss chance simply gains another
        // (1 - 1/540) factor per extra level. If the index were not clamped this would index off
        // the end of the table (or, worse, silently read a different rate).
        for (var extra = 1; extra <= 6; extra++)
        {
            var expectedPNone = pNoneAtNine * Math.Pow(1.0 - 1.0 / level9Denominator, extra);
            Assert.Equal(expectedPNone, 1.0 - doom.ProbabilityOverRun(Treads, deepestTabulated + extra), 12);
        }

        // Deeper is never worse, and it never saturates to certainty at realistic depths.
        Assert.True(doom.ProbabilityOverRun(Treads, 50) > doom.ProbabilityOverRun(Treads, 20));
        Assert.InRange(doom.ProbabilityOverRun(Treads, 50), 0.0, 1.0);
    }

    // ------------------------------------------------------- ExpectedCompletionsForRuns

    // The file's comment says the per-run sum "reduces to the flat per-level expectation when every
    // run reaches the same depth". That is a claim, so here it is as a test: with n runs all at
    // depth d, runs / sum-of-P collapses to 1 / P(item | d), independent of n.
    [Fact]
    public void ExpectedCompletionsForRuns_reduces_to_the_flat_per_run_expectation_at_a_uniform_depth()
    {
        var doom = Doom();

        foreach (var (item, firstLevel, _) in new[] { ClothTable, EyeTable, TreadsTable, PetTable })
        {
            for (var depth = firstLevel; depth <= 12; depth++)
            {
                var flat = 1.0 / doom.ProbabilityOverRun(item, depth);

                foreach (var runCount in new[] { 1, 2, 7, 100, 5000 })
                {
                    var depths = Enumerable.Repeat(depth, runCount).ToList();
                    var actual = doom.ExpectedCompletionsForRuns(item, depths);

                    Assert.NotNull(actual);
                    // Relative comparison: the flat figures span 2500 down to ~14, so a fixed
                    // absolute tolerance would be meaningless across the range.
                    Assert.Equal(1.0, actual!.Value / flat, 9);
                }
            }
        }
    }

    [Fact]
    public void ExpectedCompletionsForRuns_at_a_uniform_depth_matches_the_published_per_level_table()
    {
        var doom = Doom();

        // Cloth is eligible only at level 2 in a depth-2 run, so the answer is exactly 2500 runs
        // however many runs were done — a concrete anchor for the reduction above.
        Assert.Equal(2500, doom.ExpectedCompletionsForRuns(Cloth, [2])!.Value, 9);
        Assert.Equal(2500, doom.ExpectedCompletionsForRuns(Cloth, Enumerable.Repeat(2, 400).ToList())!.Value, 9);

        // Treads at a steady depth 8: the strategy's own comment states 1 in 160 runs.
        var treadsAtEight = doom.ExpectedCompletionsForRuns(Treads, Enumerable.Repeat(8, 25).ToList());
        Assert.NotNull(treadsAtEight);
        Assert.Equal(160, treadsAtEight!.Value, 0);

        // ...and at a steady depth 6, 1 in 305.
        var treadsAtSix = doom.ExpectedCompletionsForRuns(Treads, Enumerable.Repeat(6, 25).ToList());
        Assert.NotNull(treadsAtSix);
        Assert.Equal(305, treadsAtSix!.Value, 0);
    }

    // The failure mode this model replaced: luck was computed from the character's max-ever depth,
    // which scored ten shallow runs as if they had all been deep delves and reported everyone as dry.
    [Fact]
    public void ExpectedCompletionsForRuns_does_not_score_mixed_depth_runs_as_if_every_run_were_the_deepest()
    {
        var doom = Doom();

        int[] mixed = [2, 2, 3, 2, 4, 2, 2, 9, 2, 3, 2];
        var deepest = mixed.Max();
        var shallowest = mixed.Min();

        var honest = doom.ExpectedCompletionsForRuns(Cloth, mixed);
        var asIfAllDeepest = doom.ExpectedCompletionsForRuns(Cloth, Enumerable.Repeat(deepest, mixed.Length).ToList());
        var asIfAllShallowest = doom.ExpectedCompletionsForRuns(Cloth, Enumerable.Repeat(shallowest, mixed.Length).ToList());

        Assert.NotNull(honest);
        Assert.NotNull(asIfAllDeepest);
        Assert.NotNull(asIfAllShallowest);

        // Shallow runs are genuinely worse odds, so the honest expectation needs MORE runs per drop
        // than the all-deepest fiction. Equality here would mean the max-depth bug is back.
        Assert.True(honest!.Value > asIfAllDeepest!.Value,
            $"mixed depths expected {honest} runs but the all-depth-{deepest} fiction expected {asIfAllDeepest}");

        // And it is strictly bracketed by the two uniform extremes rather than pinned to either.
        Assert.True(honest.Value < asIfAllShallowest!.Value,
            $"mixed depths expected {honest} runs, which should beat the all-depth-{shallowest} case {asIfAllShallowest}");

        // The magnitude matters, not just the direction: this is the difference between "you are
        // 22x dry" and "you are on pace".
        Assert.True(honest.Value / asIfAllDeepest.Value > 5,
            $"the max-depth fiction understated the expectation by only {honest.Value / asIfAllDeepest.Value:0.##}x");
    }

    [Fact]
    public void ExpectedCompletionsForRuns_is_runs_over_the_summed_per_run_probability()
    {
        var doom = Doom();

        int[] depths = [2, 5, 9, 13, 4, 7];
        var summedProbability = depths.Sum(d => doom.ProbabilityOverRun(Treads, d));

        var actual = doom.ExpectedCompletionsForRuns(Treads, depths);

        Assert.NotNull(actual);
        Assert.Equal(depths.Length / summedProbability, actual!.Value, 9);
    }

    [Fact]
    public void ExpectedCompletionsForRuns_returns_null_rather_than_guessing()
    {
        var doom = Doom();

        // Not a modelled item — the caller falls back to the flat stored rate.
        Assert.Null(doom.ExpectedCompletionsForRuns(NotADoomItem, [6, 7, 8]));
        // No runs to attribute (this is what keeps global all-player pages from assuming a depth).
        Assert.Null(doom.ExpectedCompletionsForRuns(Cloth, []));
        // Runs exist but none of them could have produced the item, so the summed probability is 0
        // and there is no finite expectation to report.
        Assert.Null(doom.ExpectedCompletionsForRuns(Treads, [1, 2, 3]));
        Assert.Null(doom.ExpectedCompletionsForRuns(Cloth, [0, 0, 1]));
    }

    // ------------------------------------------------------------------- EstimateDepth

    [Fact]
    public void EstimateDepth_returns_the_assumed_average_when_nothing_proves_a_deeper_run()
    {
        var doom = Doom();

        Assert.Equal(DoomLootStrategy.AssumedAverageDepth, doom.EstimateDepth([]));
        Assert.Equal(DoomLootStrategy.AssumedAverageDepth, doom.EstimateDepth([new ClaimDrop("Coins", 12_345)]));

        // Tear quantity is deliberately ignored however large — the discarded inference read
        // systematically low (~4.8 against a real ~7) and is never coming back.
        Assert.Equal(DoomLootStrategy.AssumedAverageDepth, doom.EstimateDepth([new ClaimDrop("Demon tears", 50)]));
        Assert.Equal(DoomLootStrategy.AssumedAverageDepth, doom.EstimateDepth([new ClaimDrop("Demon tears", 100_000)]));
    }

    // EstimateDepth is max(AssumedAverageDepth, provenFloor). Each claimed unique proves a floor
    // equal to the shallowest level it can roll at: treads 4, eye 3, cloth 2, the pet Dom 6.
    //
    // NOTE: AssumedAverageDepth is currently 6, which is >= every floor, so today all four cases
    // reduce to 6 and the floors are not independently observable through this method. The test is
    // written against max(assumed, floor) rather than the literal 6 so that it starts exercising
    // the floors the moment AssumedAverageDepth is lowered. See
    // Proven_floors_match_the_shallowest_level_each_unique_can_roll_at for the independent check
    // that the floor constants themselves are right.
    [Theory]
    [InlineData(Treads, 4)]
    [InlineData(Eye, 3)]
    [InlineData(Cloth, 2)]
    [InlineData(Pet, 6)]
    public void EstimateDepth_returns_the_greater_of_the_assumed_average_and_the_proven_floor(string item, int provenFloor)
    {
        var doom = Doom();

        var expected = Math.Max(DoomLootStrategy.AssumedAverageDepth, provenFloor);

        Assert.Equal(expected, doom.EstimateDepth([new ClaimDrop(item, 1)]));
        // Never below the floor the claim proves, whatever else is in the payload.
        Assert.True(doom.EstimateDepth([new ClaimDrop(item, 1)]) >= provenFloor);
        Assert.True(doom.EstimateDepth([new ClaimDrop("Demon tears", 60), new ClaimDrop(item, 1)]) >= provenFloor);
    }

    // Guards the assumption the test above rests on. If AssumedAverageDepth drops below 6 the
    // floors become load-bearing and the theory above turns into a real test rather than a
    // tautology — this assertion is the signal that that has happened.
    [Fact]
    public void The_assumed_average_depth_currently_masks_every_proven_floor()
    {
        var highestFloor = new[] { ClothTable, EyeTable, TreadsTable, PetTable }.Max(t => t.FirstLevel);

        Assert.Equal(6, DoomLootStrategy.AssumedAverageDepth);
        Assert.True(DoomLootStrategy.AssumedAverageDepth >= highestFloor,
            $"AssumedAverageDepth ({DoomLootStrategy.AssumedAverageDepth}) has dropped below the highest "
            + $"proven floor ({highestFloor}); EstimateDepth's floor branch is now observable and "
            + "EstimateDepth_returns_the_greater_of_the_assumed_average_and_the_proven_floor is now "
            + "asserting real behaviour. Nothing is broken - re-read both tests.");
    }

    // The floor a unique proves is only correct if it equals the shallowest level that unique can
    // actually roll at. That is derived here from ProbabilityOverRun rather than from the private
    // rate arrays, so the two halves of the strategy have to agree.
    [Theory]
    [InlineData(Treads, 4)]
    [InlineData(Eye, 3)]
    [InlineData(Cloth, 2)]
    [InlineData(Pet, 6)]
    public void Proven_floors_match_the_shallowest_level_each_unique_can_roll_at(string item, int provenFloor)
    {
        var doom = Doom();

        var shallowestEligible = Enumerable.Range(1, 12).First(depth => doom.ProbabilityOverRun(item, depth) > 0);

        Assert.Equal(provenFloor, shallowestEligible);
    }

    [Fact]
    public void EstimateDepth_takes_the_deepest_floor_across_every_drop_in_a_claim()
    {
        var doom = Doom();

        // All three uniques in one claim: the deepest floor (treads, 4) wins, and the result is
        // still max(assumed, 4).
        var everything = new[]
        {
            new ClaimDrop(Cloth, 1), new ClaimDrop(Eye, 1), new ClaimDrop(Treads, 1), new ClaimDrop("Demon tears", 210)
        };

        Assert.Equal(Math.Max(DoomLootStrategy.AssumedAverageDepth, 4), doom.EstimateDepth(everything));
        Assert.True(doom.EstimateDepth(everything) >= 4);
    }

    [Fact]
    public void EstimateDepth_matches_uniques_case_insensitively_and_within_longer_names()
    {
        var doom = Doom();

        // The strategy matches uniques by case-insensitive substring, so a suffixed or differently
        // cased payload name still proves the same floor.
        Assert.Equal(doom.EstimateDepth([new ClaimDrop(Treads, 1)]),
            doom.EstimateDepth([new ClaimDrop("avernic treads", 1)]));
        Assert.Equal(doom.EstimateDepth([new ClaimDrop(Treads, 1)]),
            doom.EstimateDepth([new ClaimDrop("Avernic treads (broken)", 1)]));

        // The pet is matched by exact name, so a longer name containing it must NOT count.
        Assert.Equal(DoomLootStrategy.AssumedAverageDepth, doom.EstimateDepth([new ClaimDrop("Dominion staff", 1)]));
    }

    [Fact]
    public void EffectiveKills_is_the_estimated_delve_depth()
    {
        var doom = Doom();

        // Doom is the one source whose EffectiveKills is a depth rather than a roll count, and the
        // two entry points must not drift apart.
        foreach (var claim in new IReadOnlyList<ClaimDrop>[]
                 {
                     [],
                     [new ClaimDrop("Demon tears", 250)],
                     [new ClaimDrop(Treads, 1)],
                     [new ClaimDrop(Cloth, 1), new ClaimDrop(Pet, 1)]
                 })
        {
            Assert.Equal(doom.EstimateDepth(claim), doom.EffectiveKills(claim));
        }
    }

    // ------------------------------------------------------------------- strategy flags

    [Fact]
    public void Doom_owns_its_rates_models_depth_and_appears_on_the_leaderboard()
    {
        var doom = Doom();

        Assert.Equal("Doom of Mokhaiotl", doom.SourceName);
        Assert.True(doom.OverridesStoredRates);
        Assert.True(doom.HasDepthModel);
        Assert.True(doom.IncludeInLeaderboard);
    }

    // ------------------------------------------------------------- RatePerEligibleRoll

    [Fact]
    public void RatePerEligibleRoll_divides_only_by_the_levels_the_item_can_drop_at()
    {
        var doom = Doom();

        // A depth-8 run rolls treads at levels 4..8, so five eligible rolls per run. The headline
        // rate is eligible rolls / expected drops - which lands inside the wiki's per-level band,
        // where dividing by every delve instead did not.
        var depths = Enumerable.Repeat(8, 20).ToList();
        var expectedRolls = depths.Count * 5;
        var expectedDrops = depths.Sum(d => doom.ProbabilityOverRun(Treads, d));

        Assert.Equal(expectedRolls / expectedDrops, doom.RatePerEligibleRoll(Treads, depths)!.Value, 9);
        Assert.Equal(5, doom.EligibleRolls(Treads, 8));
        Assert.Equal(0, doom.EligibleRolls(Treads, 3));
        Assert.Equal(0, doom.EligibleRolls(NotADoomItem, 9));

        // Levels past 9 keep rolling at the level-9 rate, so they keep counting as eligible rolls.
        Assert.Equal(9, doom.EligibleRolls(Treads, 12));

        Assert.Null(doom.RatePerEligibleRoll(NotADoomItem, depths));
        Assert.Null(doom.RatePerEligibleRoll(Treads, []));
    }
}
