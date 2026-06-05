using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Ingest.Audit;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Interfaces.Repositories;

public interface ILootLogRepository
{
    Task<List<LootLogCharacterSummary>> GetCharactersWithLoot(bool includeHidden = false);
    Task<IngestLogResult> GetIngestLog(IngestLogQuery query);
    Task<LootLogSearchResult> SearchLootLog(int characterId, LootLogQuery query);
    Task<LootSourceDetail> GetSourceDetail(int characterId, string sourceName, int pageNumber, int pageSize);
    Task<LootSourceDetail> GetSourceDetailKillsPage(int characterId, string sourceName, int pageNumber, int pageSize);

    // Kill Sessions: consecutive kills grouped into play sessions (a gap > LootFeedGrouping.MaxGap
    // starts a new one), paged newest-first. GetSessionKills returns one session's individual kills.
    Task<LootSourceSessions> GetSourceSessions(int characterId, string sourceName, int pageNumber, int pageSize);
    Task<List<LootKillEntry>> GetSessionKills(int characterId, string sourceName, int sessionNo);

    // Cross-source play-session history for a character: per-source runs interleaved
    // newest-first and paged. Expand reuses GetSessionKills (session_no is per-source).
    Task<CharacterSessionHistory> GetCharacterSessions(int characterId, int pageNumber, int pageSize);

    // Data-dense, server-side-sortable per-character sources table (one aggregated row per
    // source) with totals across the full matching set.
    Task<SourceTable> GetCharacterSourceTable(int characterId, LootLogQuery query);
    Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> GetAllFeedTiers(int countPerTier, LootFeedScope scope = LootFeedScope.Main, IReadOnlySet<LootFeedTier>? requestedTiers = null);
    Task DeleteAllForCharacter(int characterId);
    Task DeleteAllForUser(int userId);

    Task<ProfileHeader?> GetProfileHeader(int characterId);
    Task<WindowStats> GetWindowStats(int characterId, DateTimeOffset? from, DateTimeOffset? to);
    Task<List<DayBucket>> GetActivityCalendar(int characterId, DateTimeOffset from, DateTimeOffset to);
    Task<MonthlyTrend> GetMonthlyTrend(int characterId, DateTimeOffset? from, DateTimeOffset to, string range);
    Task<CharacterDayFeed> GetCharacterDayFeed(int characterId, DateOnly day);
    Task<PersonalRecords> GetPersonalRecords(int characterId);
    Task<SourceCollection> GetSourceCollection(int characterId, string sourceName);
    Task<SourcePopoverData> GetSourcePopover(int characterId, string sourceName);
    Task<FirstTimeFeed> GetFirstTimeFeed(int characterId, DateTimeOffset? before, int pageSize);
    Task<TopItemsList> GetTopItems(int characterId, int limit);
}
