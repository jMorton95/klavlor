using KlavLor.Application.Features.Loot.SourceModels;

namespace KlavLor.UnitTests;

// A raid chest lists each unique as its SHARE of the unique table, not a per-raid probability: a CoX
// twisted bow shown as 2/60 is ~3% of uniques, not ~1 in 30 raids. RaidUniqueShareStrategy scales
// those shares by the average completions per unique a single player sees. Tertiary rolls (pets,
// dust, thread, kits) are already per-completion and pass through untouched.
//
// A share is identified by the item's NAME or by its denominator, either sufficient. The name test
// exists because the denominator one silently failed in production: the wiki restructured the CoX
// table from x/69 to x/60 (normal) and x/56 (challenge mode), the hourly drop-rate sync stored the
// new numbers, and every CoX unique reverted to its raw share — a twisted bow reading "1/30 raids"
// against ~1/960. Nothing in our code had changed, and no test noticed, because every test pinned
// the denominator it was asserting about.
public sealed class RaidAndDefaultStrategyTests
{
    // An item the raids do not model, for the cases where the name is irrelevant to the assertion.
    private const string Anything = "Bandos chestplate";

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
        Assert.Equal(expected, new DefaultSourceLootStrategy().ExpectedCompletions(Anything, numerator, denominator, rolls), 9);
    }

    [Fact]
    public void An_absent_or_nonsensical_denominator_yields_no_finite_expectation()
    {
        var strategy = new DefaultSourceLootStrategy();

        // double.MaxValue is the sentinel SourceLootService.EffectiveRate turns into "no rate".
        Assert.Equal(double.MaxValue, strategy.ExpectedCompletions(Anything, 1, 0, 1));
        Assert.Equal(double.MaxValue, strategy.ExpectedCompletions(Anything, 1, -5, 1));
    }

    [Fact]
    public void A_zero_or_negative_roll_count_or_numerator_is_floored_at_one()
    {
        var strategy = new DefaultSourceLootStrategy();

        // Math.Max(1, ...) on both, so a missing rolls column behaves like one roll rather than
        // producing an infinite expectation.
        Assert.Equal(500, strategy.ExpectedCompletions(Anything, 1, 500, 0), 9);
        Assert.Equal(500, strategy.ExpectedCompletions(Anything, 1, 500, -3), 9);
        Assert.Equal(500, strategy.ExpectedCompletions(Anything, 0, 500, 1), 9);
        Assert.Equal(500, strategy.ExpectedCompletions(Anything, -2, 500, 1), 9);
    }

    // ------------------------------------------------------------ RaidUniqueShareStrategy

    // The three raid strategies, restated here independently of their own constructor arguments so a
    // change to a completions-per-unique factor, a unique list or a share denominator has to be made
    // twice. The denominators are the tables the wiki publishes TODAY (August 2026); 69 is the CoX
    // table it published before then.
    private static readonly (RaidUniqueShareStrategy Strategy, string SourceName,
        double CompletionsPerUnique, string Unique, int[] TableDenominators)[] Raids =
    [
        (new ChambersOfXericStrategy(), "Chambers of Xeric", 32, "Twisted bow", [69, 60, 56]),
        (new TombsOfAmascutStrategy(), "Tombs of Amascut", 21, "Osmumten's fang", [24]),
        (new TheatreOfBloodStrategy(), "Theatre of Blood", 36, "Scythe of Vitur (uncharged)", [19, 18])
    ];

    [Fact]
    public void A_raid_claim_is_one_completion_and_the_raid_is_not_depth_modelled()
    {
        foreach (var (strategy, sourceName, _, unique, _) in Raids)
        {
            Assert.Equal(sourceName, strategy.SourceName);
            Assert.Equal(1, strategy.EffectiveKills([new ClaimDrop(unique, 1), new ClaimDrop("Coins", 500)]));
            // Raids' luck IS computable, unlike Doom's — they just need this normalisation.
            Assert.True(strategy.IncludeInLeaderboard);
            // EffectiveKills of 1 is a roll count, NOT a depth. Confusing the two had the character
            // page announcing "790 delves across 790 runs" for Chambers of Xeric.
            Assert.False(strategy.HasDepthModel, $"{sourceName} must not be depth-modelled");
            Assert.False(strategy.OverridesStoredRates, $"{sourceName} must still trust its stored rates");
            Assert.Null(strategy.ExpectedCompletionsForRuns(unique, [1, 1, 1]));
        }
    }

    [Fact]
    public void A_unique_table_share_is_scaled_by_the_completions_per_unique()
    {
        foreach (var (strategy, sourceName, completionsPerUnique, unique, denominators) in Raids)
        foreach (var tableDenominator in denominators)
        {
            const int numerator = 3;
            var flatShare = (double)tableDenominator / numerator;

            var actual = strategy.ExpectedCompletions(unique, numerator, tableDenominator, rolls: 1);

            Assert.Equal(flatShare * completionsPerUnique, actual, 9);
            // The whole point: the scaled answer is far larger than the raw share, which is what a
            // hand-rolled call site produced.
            Assert.True(actual > flatShare * 10, $"{sourceName} share {numerator}/{tableDenominator} was not scaled");
        }
    }

    // THE REGRESSION. Every unique on the list is scaled whatever denominator its share arrives on,
    // including one no strategy declares. This is what makes the model survive the wiki renumbering
    // its table, which is exactly what broke Chambers of Xeric in production.
    [Fact]
    public void Every_declared_unique_is_scaled_on_a_denominator_no_strategy_knows_about()
    {
        // A denominator deliberately absent from every strategy's list — i.e. the wiki has
        // restructured the table since this code was written.
        const int unknownDenominator = 137;

        foreach (var (strategy, sourceName, completionsPerUnique, _, denominators) in Raids)
        {
            Assert.DoesNotContain(unknownDenominator, denominators);

            foreach (var item in strategy.UniqueItems)
            {
                var actual = strategy.ExpectedCompletions(item, 2, unknownDenominator, rolls: 1);

                Assert.Equal(unknownDenominator / 2.0 * completionsPerUnique, actual, 9);
                Assert.True(actual > unknownDenominator, $"{sourceName} item {item} lost its share scaling");
            }
        }
    }

    // The live CoX shapes, item by item, so this file fails if the scaling ever silently disengages
    // again for the numbers the drop-rate sync is actually storing. 2/60 is a twisted bow on the
    // normal table: 30 raids' worth of unique table, one unique per ~32 raids, so ~960 raids.
    [Theory]
    [InlineData("Twisted bow", 2, 60, 960)]
    [InlineData("Twisted bow", 2, 56, 896)]
    [InlineData("Dexterous prayer scroll", 14, 60, 137.142857142857)]
    [InlineData("Dexterous prayer scroll", 12, 56, 149.333333333333)]
    [InlineData("Ancestral hat", 4, 60, 480)]
    [InlineData("Kodai insignia", 2, 60, 960)]
    public void The_chambers_of_xeric_shares_the_wiki_publishes_today_all_scale(
        string item, int numerator, int denominator, double expected)
    {
        Assert.Equal(expected, new ChambersOfXericStrategy().ExpectedCompletions(item, numerator, denominator, 1), 6);
    }

    [Theory]
    // Tombs of Amascut, x/24.
    [InlineData("Tombs of Amascut", "Osmumten's fang", 7, 24, 72)]
    [InlineData("Tombs of Amascut", "Tumeken's shadow (uncharged)", 1, 24, 504)]
    [InlineData("Tombs of Amascut", "Elidinis' ward", 3, 24, 168)]
    // Theatre of Blood, x/19 normal and x/18 hard mode.
    [InlineData("Theatre of Blood", "Scythe of Vitur (uncharged)", 1, 19, 684)]
    [InlineData("Theatre of Blood", "Scythe of Vitur (uncharged)", 1, 18, 648)]
    [InlineData("Theatre of Blood", "Avernic defender hilt", 8, 19, 85.5)]
    public void The_other_raids_shares_scale_at_the_rates_the_wiki_publishes_today(
        string source, string item, int numerator, int denominator, double expected)
    {
        var strategy = Raids.Single(r => r.SourceName == source).Strategy;
        Assert.Equal(expected, strategy.ExpectedCompletions(item, numerator, denominator, 1), 6);
    }

    // The collection log spells it "Scythe of vitur (uncharged)" and RuneLite "Scythe of Vitur
    // (uncharged)". Both reach this maths, so the match cannot be case-sensitive.
    [Fact]
    public void A_unique_is_matched_whatever_case_the_name_arrives_in()
    {
        var tob = new TheatreOfBloodStrategy();

        Assert.Equal(tob.ExpectedCompletions("Scythe of Vitur (uncharged)", 1, 137, 1),
                     tob.ExpectedCompletions("scythe of vitur (uncharged)", 1, 137, 1), 9);
    }

    [Fact]
    public void A_tertiary_roll_is_already_per_completion_and_passes_through_unscaled()
    {
        foreach (var (strategy, sourceName, _, _, denominators) in Raids)
        {
            // Pets and other tertiaries use their own denominator, which is not the unique table's
            // total weight, and are not on the unique list, so they must not be scaled.
            var nonTableDenominator = Enumerable.Range(2, 5000).First(d => !denominators.Contains(d));

            Assert.Equal(nonTableDenominator,
                strategy.ExpectedCompletions("Olmlet", 1, nonTableDenominator, rolls: 1), 9);
            _ = sourceName;
        }
    }

    // The real CoX tertiaries, at their live rates, none of which is on the unique list or a table
    // denominator. Scaling any of these would report a pet as a ~1,700-raid grind.
    [Theory]
    [InlineData("Olmlet", 1, 53)]
    [InlineData("Metamorphic dust", 1, 400)]
    [InlineData("Twisted ancestral colour kit", 1, 75)]
    [InlineData("Clue scroll (elite)", 1, 12)]
    [InlineData("Torn prayer scroll", 1, 33)]
    public void A_chambers_of_xeric_tertiary_keeps_its_stored_rate(string item, int numerator, int denominator)
    {
        Assert.Equal((double)denominator / numerator,
            new ChambersOfXericStrategy().ExpectedCompletions(item, numerator, denominator, 1), 9);
    }

    [Fact]
    public void An_item_the_raid_does_not_model_is_not_scaled_by_its_name()
    {
        var cox = new ChambersOfXericStrategy();

        // Neither on the unique list nor on a table denominator.
        Assert.Equal(200.0, cox.ExpectedCompletions("Bandos chestplate", 1, 200, 1), 9);
        // A null item name (a source-wide question) falls back to the denominator test alone.
        Assert.Equal(200.0, cox.ExpectedCompletions(null, 1, 200, 1), 9);
        Assert.Equal(60.0 * 32, cox.ExpectedCompletions(null, 1, 60, 1), 9);
    }

    [Fact]
    public void A_raid_with_no_usable_denominator_still_has_no_finite_expectation()
    {
        // The unscaled sentinel must survive the multiplication, not become a finite-looking number.
        Assert.Equal(double.MaxValue, new ChambersOfXericStrategy().ExpectedCompletions("Twisted bow", 1, 0, 1));
        Assert.Equal(double.MaxValue, new ChambersOfXericStrategy().ExpectedCompletions("Twisted bow", 1, -5, 1));
    }

    // The declared lists are the right size and free of blanks. Cheap guard against a typo, which
    // fails open — the item just quietly stops being scaled.
    [Fact]
    public void The_declared_unique_lists_are_the_expected_size_and_free_of_blanks()
    {
        Assert.Equal(12, new ChambersOfXericStrategy().UniqueItems.Count);
        Assert.Equal(7, new TombsOfAmascutStrategy().UniqueItems.Count);
        Assert.Equal(7, new TheatreOfBloodStrategy().UniqueItems.Count);

        foreach (var (strategy, sourceName, _, _, _) in Raids)
        foreach (var item in strategy.UniqueItems)
            Assert.False(string.IsNullOrWhiteSpace(item), $"{sourceName} has a blank unique name");
    }
}
