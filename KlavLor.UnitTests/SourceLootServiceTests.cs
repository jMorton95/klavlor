using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.UnitTests;

// CLAUDE.md: "SourceLootService is the ONLY place expected kill counts are computed. Never derive a
// rate from numerator/denominator/rolls at a call site." That rule exists because hand-rolled maths
// shipped once and made the same drop read "dry as the desert" on one page and "lucky" on another —
// the call site skipped raid unique-table scaling AND the admin rate modifiers.
//
// These tests pin the two things that made the call site wrong: the source model normalisation and
// the modifier, both applied INSIDE the facade.
public sealed class SourceLootServiceTests
{
    private const string Doom = "Doom of Mokhaiotl";
    private const string Cloth = "Mokhaiotl cloth";
    private const string Cox = "Chambers of Xeric";
    private const string PrayerScroll = "Dexterous prayer scroll";
    private const string OrdinarySource = "Vorkath";
    private const string OrdinaryItem = "Draconic visage";

    private static SourceLootService Service(ISourceRateModifierCache? modifiers = null) =>
        new(AllStrategies(), modifiers ?? new NoRateModifiers());

    private static List<ISourceLootStrategy> AllStrategies() =>
    [
        new DefaultSourceLootStrategy(),
        new DoomLootStrategy(),
        new ChambersOfXericStrategy(),
        new TombsOfAmascutStrategy(),
        new TheatreOfBloodStrategy()
    ];

    // ------------------------------------------------------------------ EffectiveRate

    [Fact]
    public void EffectiveRate_reports_the_flat_rate_for_an_ordinary_source()
    {
        var rate = Service().EffectiveRate(OrdinarySource, OrdinaryItem, numerator: 1, denominator: 540, rolls: 1);

        Assert.NotNull(rate);
        Assert.Equal(540, rate!.Value.ExpectedKc, 9);
        Assert.Equal("1/540", rate.Value.Rarity);
    }

    [Fact]
    public void EffectiveRate_folds_the_roll_count_into_the_rate()
    {
        // Two rolls per kill halves the expected kill count.
        var rate = Service().EffectiveRate(OrdinarySource, OrdinaryItem, 1, 540, rolls: 2);

        Assert.NotNull(rate);
        Assert.Equal(270, rate!.Value.ExpectedKc, 9);
        Assert.Equal("1/270", rate.Value.Rarity);
    }

    [Fact]
    public void EffectiveRate_returns_null_when_there_is_no_usable_rate()
    {
        var svc = Service();

        // No stored denominator at all.
        Assert.Null(svc.EffectiveRate(OrdinarySource, OrdinaryItem, null, null, 1));
        Assert.Null(svc.EffectiveRate(OrdinarySource, OrdinaryItem, 1, 0, 1));
        // A rate-owning source with no run depths to work from: no fallback to the stored value.
        Assert.Null(svc.EffectiveRate(Doom, "Demon tears", 1, 15, 1));
        Assert.Null(svc.EffectiveRate(Doom, Cloth, 1, 540, 1));
    }

    // THE single-path rule: the numeric answer and the displayed answer must be the same
    // computation, so a page that shows the string and a leaderboard that sorts on the number
    // cannot disagree.
    [Fact]
    public void EffectiveRate_and_ExpectedCompletions_are_the_same_computation()
    {
        var modifiers = new FixedRateModifier(Cox, PrayerScroll, 1.5);
        var svc = Service(modifiers);

        foreach (var (source, item, num, den, rolls) in new (string, string, int, int, int)[]
                 {
                     (OrdinarySource, OrdinaryItem, 1, 5000, 1),
                     (OrdinarySource, OrdinaryItem, 3, 5000, 2),
                     (Cox, PrayerScroll, 20, 69, 1),
                     (Cox, "Olmlet", 1, 53, 1),
                     ("Tombs of Amascut", "Elidinis' ward", 3, 24, 1),
                     ("Theatre of Blood", "Scythe of vitur", 2, 19, 1)
                 })
        {
            var expected = svc.ExpectedCompletions(source, item, num, den, rolls);
            var rate = svc.EffectiveRate(source, item, num, den, rolls);

            Assert.NotNull(rate);
            Assert.Equal(expected, rate!.Value.ExpectedKc, 9);
        }
    }

    // ------------------------------------------------- admin modifiers live INSIDE the facade

    [Fact]
    public void An_admin_item_modifier_scales_the_rate_reported_by_the_facade()
    {
        const double multiplier = 2.5;

        var plain = Service().ExpectedCompletions(OrdinarySource, OrdinaryItem, 1, 540, 1);
        var modified = Service(new FixedRateModifier(OrdinarySource, OrdinaryItem, multiplier))
            .ExpectedCompletions(OrdinarySource, OrdinaryItem, 1, 540, 1);

        Assert.Equal(540, plain, 9);
        Assert.Equal(540 * multiplier, modified, 9);
    }

    [Fact]
    public void An_admin_source_wide_modifier_scales_every_item_at_that_source()
    {
        var svc = Service(new FixedRateModifier(OrdinarySource, item: null, multiplier: 0.5));

        Assert.Equal(270, svc.ExpectedCompletions(OrdinarySource, OrdinaryItem, 1, 540, 1), 9);
        Assert.Equal(500, svc.ExpectedCompletions(OrdinarySource, "Dragon bones", 1, 1000, 1), 9);
        // ...and leaves other sources alone.
        Assert.Equal(540, svc.ExpectedCompletions("Zulrah", OrdinaryItem, 1, 540, 1), 9);
    }

    // This is the exact shape of the shipped bug: a call site that computed denominator/numerator by
    // hand got 3.45 raids for a CoX prayer scroll. The facade's answer is two normalisations away
    // from that — the unique-table share scaling, and then the admin modifier on top.
    [Fact]
    public void The_facade_answer_differs_from_hand_rolled_maths_by_both_the_raid_scaling_and_the_modifier()
    {
        const double coxCompletionsPerUnique = 32;
        const double adminMultiplier = 1.5;

        // What a call site would have computed from numerator/denominator/rolls directly.
        var handRolled = 69.0 / 20.0;

        var facadeNoModifier = Service().ExpectedCompletions(Cox, PrayerScroll, 20, 69, 1);
        var facadeWithModifier = Service(new FixedRateModifier(Cox, PrayerScroll, adminMultiplier))
            .ExpectedCompletions(Cox, PrayerScroll, 20, 69, 1);

        Assert.Equal(handRolled * coxCompletionsPerUnique, facadeNoModifier, 9);
        Assert.Equal(handRolled * coxCompletionsPerUnique * adminMultiplier, facadeWithModifier, 9);

        // ~110 raids versus the hand-rolled ~3.45 - a 32x error, which is what "dry as the desert on
        // one page and lucky on another" actually looked like.
        Assert.True(facadeNoModifier / handRolled > 30);
    }

    [Fact]
    public void An_admin_modifier_scales_the_depth_derived_rate_too()
    {
        // Depth-modelled sources ignore the stored flat rate entirely, so the modifier has to be
        // applied on the depth-derived branch as well or Doom silently escapes admin overrides.
        int[] depths = [7, 7, 7, 8, 6];

        var plain = Service().ExpectedCompletions(Doom, Cloth, 1, 0, 1, depths);
        var modified = Service(new FixedRateModifier(Doom, Cloth, 3.0))
            .ExpectedCompletions(Doom, Cloth, 1, 0, 1, depths);

        Assert.Equal(new DoomLootStrategy().ExpectedCompletionsForRuns(Cloth, depths)!.Value, plain, 9);
        Assert.Equal(plain * 3.0, modified, 9);
    }

    [Fact]
    public void An_admin_modifier_scales_the_displayed_rarity_string_as_well_as_the_number()
    {
        // A modifier that only moved the number would leave the rate column disagreeing with the
        // luck figure printed beside it.
        var plain = Service().EffectiveRate(OrdinarySource, OrdinaryItem, 1, 300, 1);
        var doubled = Service(new FixedRateModifier(OrdinarySource, OrdinaryItem, 2.0))
            .EffectiveRate(OrdinarySource, OrdinaryItem, 1, 300, 1);

        Assert.Equal("1/300", plain!.Value.Rarity);
        Assert.Equal("1/600", doubled!.Value.Rarity);
    }

    // Every surface named in CLAUDE.md reads its rate through this one call, so "the same drop on a
    // different page" is the same arguments through the same method. Pinning that the answer depends
    // only on the arguments (and not on any per-call-site state) is what makes that guarantee real.
    [Fact]
    public void The_same_drop_reads_identically_however_many_times_it_is_asked_for()
    {
        var svc = Service(new FixedRateModifier(Cox, PrayerScroll, 1.25));

        var answers = Enumerable.Range(0, 5)
            .Select(_ => svc.EffectiveRate(Cox, PrayerScroll, 20, 69, 1))
            .ToList();

        Assert.All(answers, a => Assert.NotNull(a));
        Assert.Single(answers.Select(a => a!.Value.ExpectedKc).Distinct());
        Assert.Single(answers.Select(a => a!.Value.Rarity).Distinct());
    }

    // ------------------------------------------------------------------- dispatch surface

    [Fact]
    public void Special_sources_are_dispatched_by_name_case_insensitively_and_everything_else_defaults()
    {
        var svc = Service();

        Assert.True(svc.HasSpecialModel(Doom));
        Assert.True(svc.HasSpecialModel("doom of mokhaiotl"));
        Assert.True(svc.HasSpecialModel(Cox));
        Assert.False(svc.HasSpecialModel(OrdinarySource));
        Assert.False(svc.HasSpecialModel(""));

        // The default strategy is keyed on the empty string and must not show up as a special model.
        Assert.DoesNotContain("", svc.SpecialSourceNames);
        Assert.Contains(Doom, svc.SpecialSourceNames);
    }

    [Fact]
    public void Only_doom_models_depth_and_only_doom_owns_its_rates()
    {
        var svc = Service();

        Assert.True(svc.HasDepthModel(Doom));
        Assert.True(svc.OverridesStoredRates(Doom));

        // Raids carry an EffectiveKills of 1 (one completion), which is a roll count, not a depth.
        // Treating "EffectiveKills is set" as a depth model had the character page announcing
        // "790 delves across 790 runs" for Chambers of Xeric.
        foreach (var source in new[] { Cox, "Tombs of Amascut", "Theatre of Blood", OrdinarySource })
        {
            Assert.False(svc.HasDepthModel(source), $"{source} must not be depth-modelled");
            Assert.False(svc.OverridesStoredRates(source), $"{source} must still trust its stored rates");
        }
    }

    [Fact]
    public void EffectiveKills_is_one_for_ordinary_and_raid_sources_and_a_depth_for_doom()
    {
        var svc = Service();
        IReadOnlyList<ClaimDrop> drops = [new ClaimDrop("Demon tears", 210)];

        Assert.Equal(1, svc.EffectiveKills(OrdinarySource, drops));
        Assert.Equal(1, svc.EffectiveKills(Cox, drops));
        Assert.Equal(DoomLootStrategy.AssumedAverageDepth, svc.EffectiveKills(Doom, drops));
    }

    // ------------------------------------------------------------------- run normalisation

    [Fact]
    public void NormaliseRuns_discards_runs_for_sources_with_no_depth_model()
    {
        var svc = Service();
        var runs = new List<KlavLor.Application.Features.Loot.Log.SourceRun>
        {
            new(1, DateTimeOffset.UnixEpoch, 1)
        };

        Assert.Empty(svc.NormaliseRuns(Cox, runs));
        Assert.Empty(svc.NormaliseRuns(OrdinarySource, runs));
        Assert.NotEmpty(svc.NormaliseRuns(Doom, runs));
        Assert.Empty(svc.NormaliseRuns(Doom, []));
    }

    [Fact]
    public void NormaliseRuns_derives_a_missing_depth_from_the_claims_own_drops()
    {
        var svc = Service();
        var runs = new List<KlavLor.Application.Features.Loot.Log.SourceRun>
        {
            // Depth already stamped by the backfill: left alone.
            new(1, DateTimeOffset.UnixEpoch, 4),
            // Not yet stamped: derived from the payload rather than degrading to a plain run count.
            new(2, DateTimeOffset.UnixEpoch, 0,
                """[{"Name":"Demon tears","ItemId":30000,"Quantity":210,"Price":0}]"""),
            // Unparseable payload must not throw.
            new(3, DateTimeOffset.UnixEpoch, 0, "not json"),
            new(4, DateTimeOffset.UnixEpoch, 0, null)
        };

        var normalised = svc.NormaliseRuns(Doom, runs);

        Assert.Equal(4, normalised[0].Depth);
        Assert.Equal(DoomLootStrategy.AssumedAverageDepth, normalised[1].Depth);
        Assert.Equal(DoomLootStrategy.AssumedAverageDepth, normalised[2].Depth);
        Assert.Equal(DoomLootStrategy.AssumedAverageDepth, normalised[3].Depth);
    }

    [Fact]
    public void An_admin_override_depth_wins_outright_over_every_stored_depth()
    {
        var svc = Service();
        var runs = new List<KlavLor.Application.Features.Loot.Log.SourceRun>
        {
            new(1, DateTimeOffset.UnixEpoch, 3),
            new(2, DateTimeOffset.UnixEpoch, 9)
        };

        Assert.All(svc.NormaliseRuns(Doom, runs, overrideDepth: 7), r => Assert.Equal(7, r.Depth));
        // Zero / negative / null are not overrides.
        Assert.Equal([3, 9], svc.NormaliseRuns(Doom, runs, overrideDepth: 0).Select(r => r.Depth));
        Assert.Equal([3, 9], svc.NormaliseRuns(Doom, runs, overrideDepth: null).Select(r => r.Depth));
    }

    [Fact]
    public void The_derivation_version_is_never_lowered()
    {
        // The backfill re-derives every special-source record whose stored version is below this,
        // so lowering it silently strands records on stale maths.
        Assert.True(SourceLootService.DerivationVersion >= 2);
    }
}
