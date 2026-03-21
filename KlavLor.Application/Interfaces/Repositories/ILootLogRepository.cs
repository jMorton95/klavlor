using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;

namespace KlavLor.Application.Interfaces.Repositories;

public interface ILootLogRepository
{
    Task<List<LootLogCharacterSummary>> GetCharactersWithLoot(bool includeHidden = false);
    Task<LootLogSearchResult> SearchLootLog(int characterId, LootLogQuery query);
    Task<LootSourceDetail> GetSourceDetail(int characterId, string sourceName, int pageNumber, int pageSize);
    Task<LootSourceDetail> GetSourceDetailKillsPage(int characterId, string sourceName, int pageNumber, int pageSize);
    Task<List<LootFeedEntry>> GetRecentFeedEntries(int count, long? minValue = null, long? maxValue = null);
    Task DeleteAllForCharacter(int characterId);
    Task DeleteAllForUser(int userId);
}
