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
// shows a half-built board. Ranking key: Tier desc, then RarityDenominator desc (rarer
// grinds win ties), then Multiple desc.
public sealed class LuckLeaderboardEntry : Entity
{
    public long Generation { get; set; }
    public int GameCharacterId { get; set; }
    public string CharacterName { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string ItemName { get; set; } = "";
    public LeaderboardBoard Board { get; set; }

    // Integer floor of Multiple — the coarse bucket that ranks first so rarity can break ties.
    public int Tier { get; set; }
    // Spoons: expected KC / observed KC (higher = luckier). Dry: observed KC / expected KC.
    public double Multiple { get; set; }

    // False = an ongoing dry streak (still waiting); ObservedKc is the current KC.
    public bool Obtained { get; set; }
    public int ObservedKc { get; set; }
    public double ExpectedKc { get; set; }
    // Rarity tiebreak within a tier — larger denominator = rarer item = ranked higher.
    public int RarityDenominator { get; set; }
}
