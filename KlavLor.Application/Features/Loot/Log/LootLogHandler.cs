using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Log;

public sealed class LootLogHandler(
    ILootLogRepository lootLogRepository,
    LootLogValidator validator,
    ICurrentUser currentUser,
    CharacterAccessChecker accessChecker)
{
    public async Task<Result<List<LootLogCharacterSummary>>> HandleCharacters()
    {
        var includeHidden = currentUser.IsAdmin;
        var characters = await lootLogRepository.GetCharactersWithLoot(includeHidden);
        return Result<List<LootLogCharacterSummary>>.Success(characters);
    }

    public async Task<Result<LootLogSearchResult>> Handle(int characterId, LootLogQuery query)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<LootLogSearchResult>.Success(new LootLogSearchResult([], []));

        var validationResult = await validator.ValidateAsync(query);
        if (!validationResult.IsValid)
            return Result<LootLogSearchResult>.Success(new LootLogSearchResult([], []));

        var result = await lootLogRepository.SearchLootLog(characterId, query);
        return Result<LootLogSearchResult>.Success(result);
    }

    public async Task<Result<LootSourceDetail>> HandleSource(int characterId, string sourceName, int pageNumber = 1, int pageSize = 25)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<LootSourceDetail>.Failure("Character not found.");

        var result = pageNumber > 1
            ? await lootLogRepository.GetSourceDetailKillsPage(characterId, sourceName, pageNumber, pageSize)
            : await lootLogRepository.GetSourceDetail(characterId, sourceName, pageNumber, pageSize);
        return Result<LootSourceDetail>.Success(result);
    }

}
