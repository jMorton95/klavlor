using KlavLor.Application.Features.Loot.Log;

namespace KlavLor.Application.Interfaces.Repositories;

// Play-session history. One of the five repositories ILootLogRepository was split into, grouped by
// consumer feature.
public interface ILootSessionRepository
{
    // Kill Sessions: consecutive kills grouped into play sessions (a gap > LootFeedGrouping.MaxGap,
    // or an overnight break that crosses a play-day boundary, starts a new one), paged newest-first.
    // GetSessionKills returns one session's individual kills.
    Task<LootSourceSessions> GetSourceSessions(int characterId, string sourceName, int pageNumber, int pageSize);
    Task<List<LootKillEntry>> GetSessionKills(int characterId, string sourceName, int sessionNo);

    // Cross-source play-session history for a character: per-source runs interleaved
    // newest-first and paged. Expand reuses GetSessionKills (session_no is per-source).
    Task<CharacterSessionHistory> GetCharacterSessions(int characterId, int pageNumber, int pageSize);
}
