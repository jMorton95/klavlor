using KlavLor.Application.Features.Loot.Leaderboard;

namespace KlavLor.Application.Interfaces.Repositories;

// Admin blacklist of sources excluded from the luck leaderboards.
public interface ILeaderboardSourceExclusionRepository
{
    // Blank term: the current exclusion list. Otherwise: matching sources (by loot data) with
    // their excluded state, so the admin can pick sources to exclude.
    Task<List<LeaderboardSourceRow>> Search(string? term, int limit);

    Task Exclude(string sourceName);
    Task Include(string sourceName);

    // Consumed by the leaderboard refresh to skip excluded sources entirely.
    Task<IReadOnlyCollection<string>> GetExcludedSourceNames();
}
