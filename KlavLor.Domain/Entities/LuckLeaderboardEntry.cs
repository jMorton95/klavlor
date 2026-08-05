namespace KlavLor.Domain.Entities;

public enum LeaderboardBoard
{
    // Luckiest first-time drops (received far faster than expected).
    Spoon,
    // Unluckiest streaks — received far slower than expected, OR still waiting.
    DryStreak
}

// One precomputed leaderboard row per (character, source, clog item) that qualifies for a
// board. Rebuilt hourly by LuckLeaderboardRefreshService into a fresh Generation; only the
// generation named by LuckLeaderboardMeta.CurrentGeneration is served, so a refresh never
// shows a half-built board. Ranked by Score descending — see LuckScore.
public sealed class LuckLeaderboardEntry : Entity
{
    public long Generation { get; set; }
    public int GameCharacterId { get; set; }
    public string CharacterName { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public LeaderboardBoard Board { get; set; }

    /// <summary>
    /// The single ranking key, blending how far off the rate the drop was with how big a grind the
    /// item represents. Replaced an integer tier plus two tiebreaks, which couldn't express "a mild
    /// streak on a very rare item beats a bigger streak on a common one". See LuckScore.For.
    /// </summary>
    public double Score { get; set; }

    // Spoons: expected rolls / observed rolls (higher = luckier). Dry: observed / expected.
    // Display only — the true ratio, never a synthetic ranking value.
    public double Multiple { get; set; }

    // False = an ongoing dry streak (still waiting); ObservedKc is the current roll count.
    public bool Obtained { get; set; }
    public int ObservedKc { get; set; }
    public double ExpectedKc { get; set; }
    // Informational: the item's stored wiki denominator, 0 for a depth-modelled source. Not the
    // rarity measure the score uses — that is ExpectedKc, which is populated for every source.
    public int RarityDenominator { get; set; }
}
