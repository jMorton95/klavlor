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
}
