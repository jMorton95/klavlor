using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Leaderboard;

// Reads a precomputed board. The heavy lifting happens hourly in
// LuckLeaderboardRefreshService, so this is a trivial ordered read.
public sealed class LuckLeaderboardHandler(ILuckLeaderboardRepository repository)
{
    public const int BoardLimit = 50;

    public Task<IReadOnlyList<LuckLeaderboardEntry>> Get(LeaderboardBoard board) =>
        repository.GetBoard(board, BoardLimit);
}
