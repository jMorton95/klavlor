namespace KlavLor.Application.Features.Loot.Leaderboard;

// One row in the admin leaderboard item-exclusion panel: an item name, how many loot drops
// reference it, and whether it is currently excluded from the boards.
public sealed record LeaderboardItemRow(string ItemName, long DropCount, bool IsExcluded);
