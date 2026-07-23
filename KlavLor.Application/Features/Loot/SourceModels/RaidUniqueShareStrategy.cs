namespace KlavLor.Application.Features.Loot.SourceModels;

// Raids (Chambers of Xeric, Tombs of Amascut, Theatre of Blood) whose chest lists each unique
// as its SHARE of the unique table (numerator/denominator where the denominator is the table's
// total weight), NOT a per-raid probability. The wiki number answers "given a unique dropped,
// which item is it" — so a CoX prayer scroll shown as 20/69 is ~29% of uniques, not ~1 in 3.45
// raids. The real per-player expected count multiplies that share by the average number of
// completions per unique a single player receives, which folds in the points-based unique
// frequency (and, for ToB, the party-size split of a single team roll).
//
// Only unique-table shares are scaled: a share is identified by its denominator matching the
// table's total weight. Tertiary rolls (pets, dust, thread) use a different denominator and are
// already per-completion, so they pass through unscaled.
//
// These sources DO appear on the leaderboard — unlike Doom, their luck is computable, it just
// needs this normalisation.
public abstract class RaidUniqueShareStrategy(string sourceName, double completionsPerUnique, int[] tableDenominators)
    : SourceLootStrategy(sourceName)
{
    // A raid claim is one completion.
    public override int EffectiveKills(IReadOnlyList<ClaimDrop> drops) => 1;

    public override double ExpectedCompletions(int numerator, int denominator, int rolls)
    {
        var flat = base.ExpectedCompletions(numerator, denominator, rolls);
        return tableDenominators.Contains(denominator) ? flat * completionsPerUnique : flat;
    }
}

// ~1 unique per 30–33 raids (solo normal); the share denominator is 69.
public sealed class ChambersOfXericStrategy() : RaidUniqueShareStrategy("Chambers of Xeric", 32, [69]);

// ~1 unique per 21 raids (~RL300 solo); the share denominator is 24.
public sealed class TombsOfAmascutStrategy() : RaidUniqueShareStrategy("Tombs of Amascut", 21, [24]);

// Team unique chance ~1/9.1 handed to one of ~4 players, so a given player's factor is
// ~9.1 × 4 ≈ 36. Share denominators are 19 (normal) and 18 (hard mode).
public sealed class TheatreOfBloodStrategy() : RaidUniqueShareStrategy("Theatre of Blood", 36, [19, 18]);
