using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Interfaces.Repositories;

public interface ILootLogRepository
{
    Task<List<LootLogCharacterSummary>> GetCharactersWithLoot(bool includeHidden = false);
    Task<LootLogSearchResult> SearchLootLog(int characterId, LootLogQuery query);
    Task<LootSourceDetail> GetSourceDetail(int characterId, string sourceName, int pageNumber, int pageSize);
    Task<LootSourceDetail> GetSourceDetailKillsPage(int characterId, string sourceName, int pageNumber, int pageSize);
    Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> GetAllFeedTiers(int countPerTier, IReadOnlySet<LootFeedTier>? requestedTiers = null);
    Task DeleteAllForCharacter(int characterId);
    Task DeleteAllForUser(int userId);

    Task<ProfileHeader?> GetProfileHeader(int characterId);
    Task<WindowStats> GetWindowStats(int characterId, DateTimeOffset? from, DateTimeOffset? to);
    Task<List<DayBucket>> GetActivityCalendar(int characterId, DateTimeOffset from, DateTimeOffset to);
    Task<PersonalRecords> GetPersonalRecords(int characterId);
    Task<Dictionary<string, int>> GetDryStreaks(int characterId, IReadOnlyList<string> sourceNames);
    Task<SourceCollection> GetSourceCollection(int characterId, string sourceName);
    Task<FirstTimeFeed> GetFirstTimeFeed(int characterId, DateTimeOffset? before, int pageSize);
    Task<TopItemsList> GetTopItems(int characterId, int limit);
}
