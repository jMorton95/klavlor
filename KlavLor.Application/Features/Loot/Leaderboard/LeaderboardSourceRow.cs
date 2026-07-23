namespace KlavLor.Application.Features.Loot.Leaderboard;

// A source row in the admin "leaderboard exclusions" panel: its name, how much loot it has
// (for context in search results), and whether it's currently excluded from the boards.
public sealed record LeaderboardSourceRow(string SourceName, long LootCount, bool IsExcluded);
