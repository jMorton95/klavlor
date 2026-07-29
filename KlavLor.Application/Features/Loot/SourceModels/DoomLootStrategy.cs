namespace KlavLor.Application.Features.Loot.SourceModels;

// Doom of Mokhaiotl. A run descends through escalating "delve" levels; loot is rolled
// independently at every level cleared and accumulated, then claimed once — so one recorded
// claim represents many rolls, not one kill. Rates (denominators, 1/x) come from the wiki's
// Module:Doom_of_Mokhaiotl_loot, indexed by min(level, 9); 0 = the item can't roll at that
// level yet. Deep delves past 8 repeat the level-9 rate.
//
// We can't read delve depth from the loot payload, so we estimate it from the claimed items:
// each unique gates a minimum depth, and the guaranteed Demon tears scale with depth (a
// stronger signal). EffectiveKills returns that estimate so a deep run contributes its true
// weight to the kill count. Accuracy is deliberately approximate.
public sealed class DoomLootStrategy() : SourceLootStrategy("Doom of Mokhaiotl")
{
    private const int MaxLevel = 9; // the level-9 rate also covers all deeper "deep delves"

    // Per-item drop-rate denominator by delve level (index 0 unused; 1..9). 0 = not eligible yet.
    private static readonly int[] Cloth  = { 0, 0, 2500, 2000, 1350, 810, 765, 720, 630, 540 };
    private static readonly int[] Eye    = { 0, 0, 0,    2000, 1350, 810, 765, 720, 630, 540 };
    private static readonly int[] Treads = { 0, 0, 0,    0,    1350, 810, 765, 720, 630, 540 };
    private static readonly int[] Pet    = { 0, 0, 0,    0,    0,    0,   1000, 750, 500, 250 };

    public override int EffectiveKills(IReadOnlyList<ClaimDrop> drops) => EstimateDepth(drops);

    // Expected runs to a first drop = 1 / P(at least one across a run to the character's depth).
    // Null when the item isn't a Doom unique or the depth is unknown, so the caller falls back.
    public override double? ExpectedCompletionsForDepth(string itemName, int depth)
    {
        var p = ProbabilityOverRun(itemName, depth);
        return p > 0 ? 1.0 / p : null;
    }

    // Deepest delve the claim proves the run reached: the max of the unique-item gates present
    // and the depth implied by the Demon tears quantity. Never below 1.
    public int EstimateDepth(IReadOnlyList<ClaimDrop> drops)
    {
        var depth = 1;
        var tears = 0;

        foreach (var d in drops)
        {
            var name = d.ItemName;
            if (Contains(name, "Avernic treads")) depth = Math.Max(depth, 4);
            else if (Contains(name, "Eye of ayak")) depth = Math.Max(depth, 3);
            else if (Contains(name, "Mokhaiotl cloth")) depth = Math.Max(depth, 2);

            if (string.Equals(name, "Dom", StringComparison.OrdinalIgnoreCase)) depth = Math.Max(depth, 6);
            if (Contains(name, "Demon tear")) tears += d.Quantity;
        }

        return Math.Max(depth, TearsToDepth(tears));
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

    // Guaranteed Demon tears accumulate ~50/110/180/260/350/450 by delve 3..8.
    private static int TearsToDepth(int tears) => tears switch
    {
        >= 450 => 8,
        >= 350 => 7,
        >= 260 => 6,
        >= 180 => 5,
        >= 110 => 4,
        >= 50 => 3,
        _ => 1
    };

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
