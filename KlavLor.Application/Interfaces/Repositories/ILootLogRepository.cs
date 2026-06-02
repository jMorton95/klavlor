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
