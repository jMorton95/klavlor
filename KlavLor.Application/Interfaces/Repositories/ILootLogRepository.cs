using KlavLor.Application.Features.Loot.Log;

namespace KlavLor.Application.Interfaces.Repositories;

public interface ILootLogRepository
{
    Task<List<LootLogUserSummary>> GetUsersWithLoot();
    Task<LootLogSearchResult> SearchLootLog(int userId, LootLogQuery query);
    Task<LootSourceDetail> GetSourceDetail(int userId, string sourceName, int pageNumber, int pageSize);
}
