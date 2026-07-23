using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Leaderboard;

// Backs the admin "leaderboard exclusions" panel: search sources and toggle whether their
// items appear on the luck boards. Exclusions take effect on the next hourly rebuild.
public sealed class LeaderboardExclusionAdminHandler(ILeaderboardSourceExclusionRepository repository)
{
    public const int SearchLimit = 40;

    public Task<List<LeaderboardSourceRow>> Search(string? term) => repository.Search(term, SearchLimit);

    public async Task<LeaderboardSourceRow> Exclude(string sourceName, long lootCount)
    {
        await repository.Exclude(sourceName);
        return new LeaderboardSourceRow(sourceName, lootCount, true);
    }

    public async Task<LeaderboardSourceRow> Include(string sourceName, long lootCount)
    {
        await repository.Include(sourceName);
        return new LeaderboardSourceRow(sourceName, lootCount, false);
    }
}
