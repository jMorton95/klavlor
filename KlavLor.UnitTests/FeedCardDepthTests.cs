using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.UnitTests;

// Both feed paths used to resolve a depth-modelled card's depth by hand, straight off the stored
// EffectiveKills, and neither applied the admin per-character override. That produced two separate
// faults: an admin who set a character's average delve depth saw the character page and the
// leaderboard change but not the feed card for the same drop, and a record the derivation backfill
// hadn't stamped yet got no rate at all.
//
// RunDepthsForClaim is the single path both now go through. These tests pin its policy, and pin the
// scale invariant that made the live card's luck figure wrong by the whole depth factor.
public sealed class FeedCardDepthTests
{
    private const string Doom = "Doom of Mokhaiotl";
    private const string Treads = "Avernic treads";
    private const string OrdinarySource = "Vorkath";

    private static SourceLootService Service() => new(
        [
            new DefaultSourceLootStrategy(),
            new DoomLootStrategy(),
            new ChambersOfXericStrategy(),
            new TombsOfAmascutStrategy(),
            new TheatreOfBloodStrategy()
        ],
        new NoRateModifiers());

    [Fact]
    public void OrdinarySource_hasNoDepthProfile()
    {
        // Raids store an EffectiveKills of 1 (a roll count, not a depth). Returning a depth for
        // them is what once had Chambers of Xeric reporting "520 delves across 520 runs".
        Assert.Null(Service().RunDepthsForClaim(OrdinarySource, claimDepth: 1, overrideDepth: null));
    }

    [Fact]
    public void AdminOverride_winsOverTheClaimsOwnDerivedDepth()
    {
        // The whole point of the override: an admin who knows the player's real average is a better
        // source of truth than what we inferred from one claim's drops.
        var depths = Service().RunDepthsForClaim(Doom, claimDepth: 4, overrideDepth: 8);

        Assert.Equal([8], depths);
    }

    [Fact]
    public void WithoutAnOverride_theClaimsOwnDepthIsUsed()
    {
        Assert.Equal([7], Service().RunDepthsForClaim(Doom, claimDepth: 7, overrideDepth: null));
    }

    [Fact]
    public void UnstampedClaim_fallsBackToTheAssumedDepthRatherThanNoRate()
    {
        // A record the backfill hasn't reached has EffectiveKills 0/null. Passing that through as
        // "no depth" made EffectiveRate return null, so the card showed no rate at all — the
        // symptom that made Doom look broken in production.
        var depths = Service().RunDepthsForClaim(Doom, claimDepth: null, overrideDepth: null);

        Assert.Equal([DoomLootStrategy.AssumedAverageDepth], depths);
        Assert.Equal(
            Service().RunDepthsForClaim(Doom, claimDepth: 0, overrideDepth: null),
            depths);
    }

    [Fact]
    public void UnstampedClaim_stillGetsAUsableRate()
    {
        // The consequence that matters: a rate exists for an unstamped Doom claim.
        var depths = Service().RunDepthsForClaim(Doom, claimDepth: null, overrideDepth: null);
        var rate = Service().EffectiveRate(Doom, Treads, null, null, rolls: 1, depths);

        Assert.NotNull(rate);
        Assert.True(rate!.Value.ExpectedKc > 0);
    }

    [Fact]
    public void ExpectedKc_isInRuns_notDelves()
    {
        // The scale invariant behind the live-card bug. The observed side of a feed card's luck
        // ratio is the run count, so ExpectedKc must be runs too. It is: a one-run profile's
        // expected value is 1/P(item per run), which is far below the per-delve denominator — if
        // this were delves, multiplying the observed side by depth (as the card used to) would have
        // been the correct thing to do instead of a depth-factor error.
        var oneRun = Service().RunDepthsForClaim(Doom, claimDepth: 8, overrideDepth: null)!;
        var expectedForOneRun = Service().EffectiveRate(Doom, Treads, null, null, 1, oneRun)!.Value.ExpectedKc;

        // Ten runs at the same depth must expect the same number of RUNS per drop as one, because
        // the per-run probability hasn't changed. That invariance is the defining property of a
        // run-denominated figure; a delve-denominated one would scale with the run count.
        var tenRuns = Enumerable.Repeat(8, 10).ToList();
        var expectedForTenRuns = Service().EffectiveRate(Doom, Treads, null, null, 1, tenRuns)!.Value.ExpectedKc;

        Assert.Equal(expectedForOneRun, expectedForTenRuns, precision: 6);
    }

    [Fact]
    public void DeeperOverride_makesTheDropCheaperInRuns()
    {
        // A deeper average clears more levels per run, so each run has more chances at the item.
        // This is what an admin changing the override should visibly do to the card's rate.
        var shallow = Service().RunDepthsForClaim(Doom, null, overrideDepth: 4)!;
        var deep = Service().RunDepthsForClaim(Doom, null, overrideDepth: 9)!;

        var shallowKc = Service().EffectiveRate(Doom, Treads, null, null, 1, shallow)!.Value.ExpectedKc;
        var deepKc = Service().EffectiveRate(Doom, Treads, null, null, 1, deep)!.Value.ExpectedKc;

        Assert.True(deepKc < shallowKc,
            $"expected a deeper delve to need fewer runs per drop, got {deepKc:N1} vs {shallowKc:N1}");
    }
}
