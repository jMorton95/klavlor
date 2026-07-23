using KlavLor.Application.Features.Loot.Leaderboard;

namespace KlavLor.Application.Interfaces.Repositories;

// Admin blacklist of items excluded from the luck leaderboards (item-level counterpart to
// ILeaderboardSourceExclusionRepository).
public interface ILeaderboardItemExclusionRepository
{
    // Blank term: the current exclusion list. Otherwise: matching items (by loot data) with
    // their excluded state, so the admin can pick items to exclude.
    Task<List<LeaderboardItemRow>> Search(string? term, int limit);

    Task Exclude(string itemName);
    Task Include(string itemName);

    // Consumed by the leaderboard refresh to skip excluded items across every source.
    Task<IReadOnlyCollection<string>> GetExcludedItemNames();
}
