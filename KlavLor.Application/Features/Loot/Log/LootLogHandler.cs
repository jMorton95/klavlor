using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Log;

public sealed class LootLogHandler(
    ILootLogRepository lootLogRepository,
    IGameCharacterRepository gameCharacterRepository,
    LootLogValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result<List<LootLogCharacterSummary>>> HandleCharacters()
    {
        var includeHidden = currentUser.IsAdmin;
        var characters = await lootLogRepository.GetCharactersWithLoot(includeHidden);
        return Result<List<LootLogCharacterSummary>>.Success(characters);
    }

    public async Task<Result<LootLogSearchResult>> Handle(int characterId, LootLogQuery query)
    {
        if (!await CanAccessCharacter(characterId))
            return Result<LootLogSearchResult>.Success(new LootLogSearchResult([], []));

        var validationResult = await validator.ValidateAsync(query);
        if (!validationResult.IsValid)
            return Result<LootLogSearchResult>.Success(new LootLogSearchResult([], []));

        var result = await lootLogRepository.SearchLootLog(characterId, query);
        return Result<LootLogSearchResult>.Success(result);
    }

    public async Task<Result<LootSourceDetail>> HandleSource(int characterId, string sourceName, int pageNumber = 1, int pageSize = 25)
    {
        if (!await CanAccessCharacter(characterId))
            return Result<LootSourceDetail>.Failure("Character not found.");

        var result = pageNumber > 1
            ? await lootLogRepository.GetSourceDetailKillsPage(characterId, sourceName, pageNumber, pageSize)
            : await lootLogRepository.GetSourceDetail(characterId, sourceName, pageNumber, pageSize);
        return Result<LootSourceDetail>.Success(result);
    }

    private async Task<bool> CanAccessCharacter(int characterId)
    {
        if (currentUser.IsAdmin)
            return true;

        var character = await gameCharacterRepository.GetById(characterId);
        if (character is null)
            return false;

        // Owner can always access their own characters
        if (character.UserId == currentUser.UserId)
            return true;

        // Others can only see visible, non-admin-hidden characters
        return character.IsVisible && !character.IsAdminHidden;
    }
}
