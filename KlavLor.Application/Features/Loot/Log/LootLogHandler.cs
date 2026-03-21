using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Log;

public sealed class LootLogHandler(
    ILootLogRepository lootLogRepository,
    LootLogValidator validator)
{
    public async Task<Result<List<LootLogUserSummary>>> HandleUsers()
    {
        var users = await lootLogRepository.GetUsersWithLoot();
        return Result<List<LootLogUserSummary>>.Success(users);
    }

    public async Task<Result<LootLogSearchResult>> Handle(int userId, LootLogQuery query)
    {
        var validationResult = await validator.ValidateAsync(query);
        if (!validationResult.IsValid)
            return Result<LootLogSearchResult>.Success(new LootLogSearchResult([], []));

        var result = await lootLogRepository.SearchLootLog(userId, query);
        return Result<LootLogSearchResult>.Success(result);
    }

    public async Task<Result<LootSourceDetail>> HandleSource(int userId, string sourceName, int pageNumber = 1, int pageSize = 25)
    {
        var result = pageNumber > 1
            ? await lootLogRepository.GetSourceDetailKillsPage(userId, sourceName, pageNumber, pageSize)
            : await lootLogRepository.GetSourceDetail(userId, sourceName, pageNumber, pageSize);
        return Result<LootSourceDetail>.Success(result);
    }
}
