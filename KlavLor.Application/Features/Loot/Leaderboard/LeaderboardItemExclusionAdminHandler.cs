using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Leaderboard;

// Backs the admin leaderboard item-exclusion panel: search items and toggle whether they appear
// on the luck boards, regardless of source. Exclusions take effect on the next hourly rebuild.
public sealed class LeaderboardItemExclusionAdminHandler(ILeaderboardItemExclusionRepository repository)
{
    public const int SearchLimit = 40;

    public Task<List<LeaderboardItemRow>> Search(string? term) => repository.Search(term, SearchLimit);

    public async Task<LeaderboardItemRow> Exclude(string itemName, long dropCount)
    {
        await repository.Exclude(itemName);
        return new LeaderboardItemRow(itemName, dropCount, true);
    }

    public async Task<LeaderboardItemRow> Include(string itemName, long dropCount)
    {
        await repository.Include(itemName);
        return new LeaderboardItemRow(itemName, dropCount, false);
    }
}
