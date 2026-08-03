using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.IntegrationTests;

// Doom's luck used to be computed from a single max-ever delve depth, which scored every run as
// if it had gone as deep as the player's deepest ever — overstating the per-run odds and reporting
// everyone as drier than they were. Expected KC is now derived from the depth of every ACTUAL run.
// These tests pin that, plus the fact that an admin rate modifier is a global baseline that
// applies on top of the depth model.
public sealed class DoomDepthModelTests
{
    private const string Doom = "Doom of Mokhaiotl";
    private const string Cloth = "Mokhaiotl cloth";

    private static SourceLootService Service(ISourceRateModifierCache? modifiers = null) =>
        new([new DefaultSourceLootStrategy(), new DoomLootStrategy()], modifiers ?? new NoModifiers());

    [Fact]
    public void Shallow_runs_are_not_scored_as_if_they_were_deep_ones()
    {
        var doom = new DoomLootStrategy();

        // Ten runs that only reached delve 2 (cloth at 1/2500) plus one deep run at delve 9
        // (1/540 per level, so a much better shot). The old max-depth model treated all eleven
        // as depth-9 runs.
        var real = new[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 9 };
        var asIfAllDeep = Enumerable.Repeat(9, real.Length).ToList();

        var expectedFromRealRuns = doom.ExpectedCompletionsForRuns(Cloth, real);
        var expectedFromMaxDepth = doom.ExpectedCompletionsForRuns(Cloth, asIfAllDeep);

        Assert.NotNull(expectedFromRealRuns);
        Assert.NotNull(expectedFromMaxDepth);

        // Shallow runs really are worse odds, so the honest expectation is MORE runs per drop.
        // Pretending every run was a deep delve is what made players look dry.
        Assert.True(expectedFromRealRuns!.Value > expectedFromMaxDepth!.Value,
            $"real={expectedFromRealRuns} should exceed max-depth={expectedFromMaxDepth}");
    }

    [Fact]
    public void A_uniform_depth_reduces_to_the_flat_rate_for_that_depth()
    {
        var doom = new DoomLootStrategy();

        // Every run at delve 2: cloth can only roll on level 2 at 1/2500, so P(run) = 1/2500 and
        // the expectation must come back as ~2500 runs regardless of how many runs there were.
        foreach (var runCount in new[] { 1, 5, 50 })
        {
            var depths = Enumerable.Repeat(2, runCount).ToList();
            var expected = doom.ExpectedCompletionsForRuns(Cloth, depths);
            Assert.NotNull(expected);
            Assert.Equal(2500, expected!.Value, 6);
        }
    }

    [Fact]
    public void Deeper_runs_mean_fewer_runs_expected_per_drop()
    {
        var doom = new DoomLootStrategy();

        var shallow = doom.ExpectedCompletionsForRuns(Cloth, [2, 2, 2, 2, 2]);
        var deep = doom.ExpectedCompletionsForRuns(Cloth, [9, 9, 9, 9, 9]);

        Assert.NotNull(shallow);
        Assert.NotNull(deep);
        Assert.True(deep!.Value < shallow!.Value);
    }

    [Fact]
    public void Non_doom_items_and_empty_run_sets_fall_back_rather_than_guess()
    {
        var doom = new DoomLootStrategy();

        Assert.Null(doom.ExpectedCompletionsForRuns("Twisted bow", [5, 6]));
        Assert.Null(doom.ExpectedCompletionsForRuns(Cloth, []));
    }

    [Fact]
    public void Doom_appears_on_the_luck_leaderboard()
    {
        // Doom used to opt out because its luck wasn't computable from a flat rate. It is now,
        // via the per-run depth model, so it must be on the board like every other source.
        Assert.True(new DoomLootStrategy().IncludeInLeaderboard);
    }

    // Loot is rolled at EVERY level a run clears, so a depth-9 run gives the cloth eight separate
    // chances (levels 2..9), not one. Expectation for a uniform depth-9 grind is therefore
    // 1 / P(at least one in a run), which is far better than the single level-9 rate of 1/540.
    private const double ClothDepth9ExpectedRuns = 111.4075330220773;

    [Fact]
    public void Depth_derived_rate_is_produced_even_with_no_stored_wiki_rate()
    {
        // Doom has no stored DropRates row, so numerator/denominator are absent. The depth model
        // must still produce a rate — this is what makes the collection log and feed show one.
        var rate = Service().EffectiveRate(Doom, Cloth, numerator: null, denominator: null, rolls: 1, runDepths: [9, 9, 9]);

        Assert.NotNull(rate);
        Assert.Equal(ClothDepth9ExpectedRuns, rate!.Value.ExpectedKc, 6);
        Assert.Equal("1/111", rate.Value.Rarity);

        // Sanity-check the model against the published per-level table rather than trusting the
        // constant: cloth rolls once per cleared level from 2 to 9.
        int[] densByLevel = [2500, 2000, 1350, 810, 765, 720, 630, 540];
        var pNone = densByLevel.Aggregate(1.0, (acc, den) => acc * (1.0 - 1.0 / den));
        Assert.Equal(1.0 / (1.0 - pNone), rate.Value.ExpectedKc, 6);
    }

    [Fact]
    public void Admin_rate_modifier_applies_on_top_of_the_depth_model()
    {
        // Rate modifiers are a global baseline: they must scale the depth-derived rate too, not
        // just flat wiki rates, so every surface agrees with the admin override.
        var doubled = Service(new FixedModifier(Doom, Cloth, 2.0))
            .EffectiveRate(Doom, Cloth, null, null, 1, runDepths: [9, 9, 9]);

        Assert.NotNull(doubled);
        Assert.Equal(ClothDepth9ExpectedRuns * 2, doubled!.Value.ExpectedKc, 6);
    }

    [Fact]
    public void Ordinary_sources_keep_the_flat_rate_when_no_depths_are_supplied()
    {
        var rate = Service().EffectiveRate("Vorkath", "Draconic visage", 1, 5000, rolls: 1);

        Assert.NotNull(rate);
        Assert.Equal(5000, rate!.Value.ExpectedKc, 6);
    }

    [Fact]
    public void Guaranteed_doom_drops_get_no_rate_instead_of_a_bogus_wiki_fallback()
    {
        // Demon tears drop every single run and carry a stored per-level rarity (1/15) that is
        // meaningless as a per-run chance. Falling back to it put "320 kills vs 15 expected —
        // 21x dry" on the leaderboard. Unmodelled Doom items must have NO usable rate.
        var svc = Service();

        Assert.Null(svc.EffectiveRate(Doom, "Demon tears", 1, 15, 1, runDepths: [7, 7, 7]));
        Assert.Null(svc.EffectiveRate(Doom, "Mokhaiotl waystone", 1, 15, 1, runDepths: [7, 7, 7]));

        // ...while the items the model does cover still resolve.
        Assert.NotNull(svc.EffectiveRate(Doom, Cloth, null, null, 1, runDepths: [7, 7, 7]));
    }

    private sealed class NoModifiers : ISourceRateModifierCache
    {
        public double GetMultiplier(string sourceName, string? itemName) => 1.0;
        public void Replace(IEnumerable<SourceRateModifierValue> modifiers) { }
    }

    private sealed class FixedModifier(string source, string item, double multiplier) : ISourceRateModifierCache
    {
        public double GetMultiplier(string sourceName, string? itemName) =>
            string.Equals(sourceName, source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(itemName, item, StringComparison.OrdinalIgnoreCase)
                ? multiplier : 1.0;

        public void Replace(IEnumerable<SourceRateModifierValue> modifiers) { }
    }
}
