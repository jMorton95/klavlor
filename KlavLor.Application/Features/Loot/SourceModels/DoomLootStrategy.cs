namespace KlavLor.Application.Features.Loot.SourceModels;

// Doom of Mokhaiotl. A run descends through escalating "delve" levels; loot is rolled
// independently at every level cleared and accumulated, then claimed once — so one recorded
// claim represents many rolls, not one kill. Rates (denominators, 1/x) come from the wiki's
// Module:Doom_of_Mokhaiotl_loot, indexed by min(level, 9); 0 = the item can't roll at that
// level yet. Deep delves past 8 repeat the level-9 rate.
//
// We can't read delve depth from the loot payload. An earlier version inferred it from the Demon
// tear quantity, but that isn't depth-proportional — the wiki gives 50 guaranteed plus a 100-300
// roll at 7/104 — and the inference read systematically low (averaging ~4.8 against a real ~7, with
// 9% of completed runs scoring depth 1). Rather than dress a guess up as a measurement, a run is now
// credited with AssumedAverageDepth, raised only where a claimed unique proves it went deeper.
// Admins override the average per character from the admin hub, which is the only way to be accurate
// until depth is captured at source.
public sealed class DoomLootStrategy() : SourceLootStrategy("Doom of Mokhaiotl")
{
    private const int MaxLevel = 9; // the level-9 rate also covers all deeper "deep delves"

    // Assumed delves per run when we have nothing better. A stated assumption, not a measurement:
    // it is deliberately visible and adjustable rather than buried in a heuristic. Admins override
    // it per character via CharacterDelveDepth.
    public const int AssumedAverageDepth = 6;

    // Per-item drop-rate denominator by delve level (index 0 unused; 1..9). 0 = not eligible yet.
    private static readonly int[] Cloth  = { 0, 0, 2500, 2000, 1350, 810, 765, 720, 630, 540 };
    private static readonly int[] Eye    = { 0, 0, 0,    2000, 1350, 810, 765, 720, 630, 540 };
    private static readonly int[] Treads = { 0, 0, 0,    0,    1350, 810, 765, 720, 630, 540 };
    private static readonly int[] Pet    = { 0, 0, 0,    0,    0,    0,   1000, 750, 500, 250 };

    public override int EffectiveKills(IReadOnlyList<ClaimDrop> drops) => EstimateDepth(drops);

    // Doom's luck IS computable, so it belongs on the leaderboard alongside every other source —
    // it just needs the per-run depth model below rather than a flat rate.
    public override bool IncludeInLeaderboard => true;

    // This strategy owns Doom's rates outright. The wiki stores per-LEVEL rarities for Doom, which
    // are not per-run chances, and the guaranteed accumulating drops (Demon tears, Mokhaiotl
    // waystone) have rates that mean nothing as a per-run probability at all. Falling back to them
    // put "320 kills vs 15 expected — 21x dry" on the board for an item players get every run.
    // Items this strategy does not model therefore have no rate, rather than a wrong one.
    public override bool OverridesStoredRates => true;

    // Doom is the one source whose EffectiveKills is a delve depth rather than a roll count.
    public override bool HasDepthModel => true;

    // Expected RUNS to a first drop, from the depth of every run the player actually did.
    //
    // Runs, not delves: a run is what a player counts, and it needs no per-item denominator. A
    // per-delve or per-roll basis would need one, because each unique becomes eligible at a
    // different level (treads at 4, cloth at 2), so "delves done" is a different number for every
    // item. ProbabilityOverRun already folds depth and eligibility in, so:
    //
    //     expected runs per drop = runs / sum of P(item | depth_r)
    //
    // At a steady depth 8 that gives treads 1 in 160 runs; at depth 6, 1 in 305. Null when the item
    // isn't a Doom unique or no run could have produced it, so the caller falls back to the flat rate.
    public override double? ExpectedCompletionsForRuns(string itemName, IReadOnlyList<int> runDepths)
    {
        if (RatesFor(itemName) is null || runDepths.Count == 0) return null;

        var expectedDrops = 0.0;
        foreach (var depth in runDepths)
            expectedDrops += ProbabilityOverRun(itemName, depth);

        return expectedDrops > 0 ? runDepths.Count / expectedDrops : null;
    }

    // The rate per ELIGIBLE ROLL — the figure directly comparable to the wiki's per-level band,
    // because it divides by only the levels this item can actually drop at. At depth 8 treads work
    // out at 1/801, which sits inside the wiki's 1/1,350-to-1/540 range; dividing by every delve
    // instead gave 1/1,281, outside the band and looking wrong. Null when not modelled.
    public double? RatePerEligibleRoll(string itemName, IReadOnlyList<int> runDepths)
    {
        if (RatesFor(itemName) is null || runDepths.Count == 0) return null;

        var expectedDrops = 0.0;
        var eligibleRolls = 0;
        foreach (var depth in runDepths)
        {
            expectedDrops += ProbabilityOverRun(itemName, depth);
            eligibleRolls += EligibleRolls(itemName, depth);
        }

        return expectedDrops > 0 && eligibleRolls > 0 ? eligibleRolls / expectedDrops : null;
    }

    // How many times this item is rolled in a run to `depth` — the levels at or past its first
    // eligible level. Levels beyond 9 keep rolling at the level-9 rate.
    public int EligibleRolls(string itemName, int depth)
    {
        var rates = RatesFor(itemName);
        if (rates is null || depth < 1) return 0;

        var count = 0;
        for (var level = 1; level <= depth; level++)
            if (rates[Math.Min(level, MaxLevel)] > 0) count++;
        return count;
    }

    // The delve depth we credit a run with: the stated assumption, raised when a claimed unique
    // proves the run must have gone deeper than that (each unique only rolls from its own level up).
    // Never an inference from tear counts — see the note at the top of this file.
    public int EstimateDepth(IReadOnlyList<ClaimDrop> drops)
    {
        var provenFloor = 1;

        foreach (var d in drops)
        {
            var name = d.ItemName;
            if (Contains(name, "Avernic treads")) provenFloor = Math.Max(provenFloor, 4);
            else if (Contains(name, "Eye of ayak")) provenFloor = Math.Max(provenFloor, 3);
            else if (Contains(name, "Mokhaiotl cloth")) provenFloor = Math.Max(provenFloor, 2);

            if (string.Equals(name, "Dom", StringComparison.OrdinalIgnoreCase)) provenFloor = Math.Max(provenFloor, 6);
        }

        return Math.Max(AssumedAverageDepth, provenFloor);
    }

    // Probability of receiving at least one of an item across a run that reached `depth`,
    // treating each eligible level as an independent trial. Feeds the delve-aware luck maths
    // once the leaderboard / collection-log consumers are routed through this strategy.
    public double ProbabilityOverRun(string itemName, int depth)
    {
        var rates = RatesFor(itemName);
        if (rates is null || depth < 1) return 0;

        var pNone = 1.0;
        for (var level = 1; level <= depth; level++)
        {
            var den = rates[Math.Min(level, MaxLevel)];
            if (den > 0) pNone *= 1.0 - 1.0 / den;
        }
        return 1.0 - pNone;
    }

    private static int[]? RatesFor(string itemName)
    {
        if (Contains(itemName, "Mokhaiotl cloth")) return Cloth;
        if (Contains(itemName, "Eye of ayak")) return Eye;
        if (Contains(itemName, "Avernic treads")) return Treads;
        if (string.Equals(itemName, "Dom", StringComparison.OrdinalIgnoreCase)) return Pet;
        return null;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
