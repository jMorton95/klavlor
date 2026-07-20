using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

// Persistence + enumeration for the precomputed luck leaderboard. The heavy per-source luck
// facts are read through ILootLogRepository.GetSourceCollection; this repository only handles
// the small enumeration and the generation-swap write path plus the served board query.
public interface ILuckLeaderboardRepository
{
    Task<IReadOnlyList<(int Id, string Name)>> GetVisibleCharacters();
    Task<IReadOnlyList<string>> GetSourcesForCharacter(int characterId);

    // The generation the next refresh will write under (max existing + 1).
    Task<long> NextGeneration();

    // Streamed insert of one unit's rows; saves and clears the change tracker to keep memory flat.
    Task InsertEntries(IReadOnlyCollection<LuckLeaderboardEntry> entries);

    // Flip the served pointer to the finished generation, then delete every superseded row.
    Task PublishGeneration(long generation);

    Task<IReadOnlyList<LuckLeaderboardEntry>> GetBoard(LeaderboardBoard board, int limit);
}
