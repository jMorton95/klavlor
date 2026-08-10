using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Leaderboard;

// Backs the admin leaderboard item-exclusion panel: search items and toggle whether they appear
// on the luck boards, regardless of source. Every toggle requests a rebuild (~a minute).
public sealed class LeaderboardItemExclusionAdminHandler(
    ILeaderboardItemExclusionRepository repository,
    RecomputeTrigger recompute)
{
    public const int SearchLimit = 40;

    public Task<List<LeaderboardItemRow>> Search(string? term) => repository.Search(term, SearchLimit);

    public async Task<LeaderboardItemRow> Exclude(string itemName, long dropCount)
    {
        await repository.Exclude(itemName);
        await recompute.LuckInputsChanged();
        return new LeaderboardItemRow(itemName, dropCount, true);
    }

    public async Task<LeaderboardItemRow> Include(string itemName, long dropCount)
    {
        await repository.Include(itemName);
        await recompute.LuckInputsChanged();
        return new LeaderboardItemRow(itemName, dropCount, false);
    }
}
