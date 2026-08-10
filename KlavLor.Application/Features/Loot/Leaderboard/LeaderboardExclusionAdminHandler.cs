using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Leaderboard;

// Backs the admin "leaderboard exclusions" panel: search sources and toggle whether their
// items appear on the luck boards. Every toggle requests a rebuild, so it lands within ~a minute.
public sealed class LeaderboardExclusionAdminHandler(
    ILeaderboardSourceExclusionRepository repository,
    RecomputeTrigger recompute)
{
    public const int SearchLimit = 40;

    public Task<List<LeaderboardSourceRow>> Search(string? term) => repository.Search(term, SearchLimit);

    public async Task<LeaderboardSourceRow> Exclude(string sourceName, long lootCount)
    {
        await repository.Exclude(sourceName);
        await recompute.LuckInputsChanged();
        return new LeaderboardSourceRow(sourceName, lootCount, true);
    }

    public async Task<LeaderboardSourceRow> Include(string sourceName, long lootCount)
    {
        await repository.Include(sourceName);
        await recompute.LuckInputsChanged();
        return new LeaderboardSourceRow(sourceName, lootCount, false);
    }
}
