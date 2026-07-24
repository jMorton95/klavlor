using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Leaderboard;

// Reads a precomputed board. The heavy lifting happens hourly in
// LuckLeaderboardRefreshService, so this is a trivial ordered read.
public sealed class LuckLeaderboardHandler(ILuckLeaderboardRepository repository)
{
    // Generous cap: rare long-grind dry streaks land at a low tier (multiple just over 1), so a
    // small limit would cut them off entirely. 200 keeps the tail (e.g. a 1/3000 item at 3500 KC)
    // visible while staying a bounded read.
    public const int BoardLimit = 200;

    public Task<IReadOnlyList<LuckLeaderboardEntry>> Get(LeaderboardBoard board) =>
        repository.GetBoard(board, BoardLimit);
}
