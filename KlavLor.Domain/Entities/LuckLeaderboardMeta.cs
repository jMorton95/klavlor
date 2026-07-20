namespace KlavLor.Domain.Entities;

// Singleton pointer to the currently-published leaderboard generation. The refresh service
// streams a new generation's rows in, then flips this pointer in one write so readers only
// ever see a complete generation, then deletes the superseded rows.
public sealed class LuckLeaderboardMeta : Entity
{
    public long CurrentGeneration { get; set; }
    public DateTimeOffset RefreshedAt { get; set; }
}
