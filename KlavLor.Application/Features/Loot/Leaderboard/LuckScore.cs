namespace KlavLor.Application.Features.Loot.Leaderboard;

/// <summary>
/// The luck leaderboard's ranking score.
///
/// Two independent things make a streak worth showing, and a single number has to carry both:
///
///   - How improbable it is. This depends ONLY on the multiple: P(still dry after m times the
///     expected rolls) is about e^-m, so 2x dry on a 1/1000 and 2x dry on a 1/5000 are equally
///     unlikely, both around 13%. Rarity does not enter here at all.
///   - How big the grind was. This is where rarity enters: reaching 2x on a 1/5000 takes 10,000
///     rolls against 2,000 for a 1/1000. It is the time actually sunk into the item.
///
/// Ranking on the multiple alone therefore ignores the grind entirely, and ranking on rolls alone
/// puts a mundane 1x streak on a very rare item above a 5x streak on a common one. The score
/// blends them:
///
///     score = multiple^1.5 * expectedRolls^(RarityBase + RarityGrowth * ln expectedRolls)
///
/// The exponent on rarity is not constant: it grows slowly with rarity, so a 1/5000 is weighted
/// slightly harder than a 1/1000 (0.486 against 0.454) and a 1/10000 harder again. That is the
/// difference between this and a flat square-root weighting, and it is what lets a 1.25x streak on
/// a 1/5000 outrank a 2x streak on a 1/1000 by a comfortable margin rather than a hair.
///
/// The exponent on the multiple is 1.5 — deliberately as high as the ordering above allows. Any
/// higher and that 1.25x-versus-2x preference inverts; any lower and genuinely extreme streaks stop
/// holding the top of the board. It sits at the boundary between the two goals on purpose.
///
/// RarityGrowth is the single tuning knob: raise it to favour rare grinds harder, take it to zero
/// for a flat square-root weighting.
/// </summary>
public static class LuckScore
{
    /// <summary>Weight on how far off the rate the drop was.</summary>
    public const double MultipleExponent = 1.5;

    /// <summary>Rarity exponent at one expected roll, before growth is applied.</summary>
    public const double RarityBase = 0.316;

    /// <summary>How fast the rarity exponent grows per natural log of expected rolls.</summary>
    public const double RarityGrowth = 0.02;

    /// <summary>
    /// An item rarer than this many expected rolls can make a board on any multiple past 1x; below
    /// it, a streak has to be worth something in gp instead (see LuckScore callers). A 1x bar on
    /// truly common drops would bury the board in noise.
    /// </summary>
    public const int MinExpectedRollsForBoard = 100;

    /// <summary>
    /// The rarity exponent for an item that takes <paramref name="expectedRolls"/> rolls on average.
    /// Grows with rarity, so rarer items are weighted progressively harder.
    /// </summary>
    public static double RarityExponent(double expectedRolls) =>
        RarityBase + RarityGrowth * Math.Log(Math.Max(1, expectedRolls));

    /// <summary>
    /// Ranking score for one entry. <paramref name="multiple"/> is how many times the expected roll
    /// count the result was — observed/expected for a dry streak, expected/observed for a spoon, so
    /// bigger is always more remarkable on either board.
    /// </summary>
    public static double For(double multiple, double expectedRolls)
    {
        if (multiple <= 0 || expectedRolls < 1) return 0;
        return Math.Pow(multiple, MultipleExponent)
             * Math.Pow(expectedRolls, RarityExponent(expectedRolls));
    }
}
