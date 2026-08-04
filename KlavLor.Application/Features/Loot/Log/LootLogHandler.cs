using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Log;

public sealed class LootLogHandler(
    ILootLogSearchRepository searchRepository,
    ILootSourceDetailRepository sourceDetailRepository,
    ILootSessionRepository sessionRepository,
    LootLogValidator validator,
    CharacterAccessChecker accessChecker)
{
    public async Task<Result<List<LootLogCharacterSummary>>> HandleCharacters()
    {
        // Always respect the visibility flags on the public drop-log grid — admins
        // included. Hidden means hidden. Admins who need to reach a hidden character
        // can still get there via the admin user-management page or a direct URL.
        var characters = await searchRepository.GetCharactersWithLoot(includeHidden: false);
        return Result<List<LootLogCharacterSummary>>.Success(characters);
    }

    public async Task<Result<LootLogSearchResult>> Handle(int characterId, LootLogQuery query)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<LootLogSearchResult>.Success(new LootLogSearchResult([], []));

        var validationResult = await validator.ValidateAsync(query);
        if (!validationResult.IsValid)
            return Result<LootLogSearchResult>.Success(new LootLogSearchResult([], []));

        var result = await searchRepository.SearchLootLog(characterId, query);
        return Result<LootLogSearchResult>.Success(result);
    }

    public async Task<Result<LootSourceDetail>> HandleSource(int characterId, string sourceName, int pageNumber = 1, int pageSize = 25)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<LootSourceDetail>.Failure("Character not found.");

        var result = pageNumber > 1
            ? await sourceDetailRepository.GetSourceDetailKillsPage(characterId, sourceName, pageNumber, pageSize)
            : await sourceDetailRepository.GetSourceDetail(characterId, sourceName, pageNumber, pageSize);
        return Result<LootSourceDetail>.Success(result);
    }

    public async Task<Result<SourceTable>> HandleSourceTable(int characterId, LootLogQuery query)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<SourceTable>.Failure("Character not found.");

        var result = await searchRepository.GetCharacterSourceTable(characterId, query);
        return Result<SourceTable>.Success(result);
    }

    public const int SessionsPageSize = 15;

    public async Task<Result<LootSourceSessions>> HandleSourceSessions(int characterId, string sourceName, int pageNumber = 1)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<LootSourceSessions>.Failure("Character not found.");

        var result = await sessionRepository.GetSourceSessions(characterId, sourceName, pageNumber, SessionsPageSize);
        return Result<LootSourceSessions>.Success(result);
    }

    public async Task<Result<List<LootKillEntry>>> HandleSessionKills(int characterId, string sourceName, int sessionNo)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<List<LootKillEntry>>.Failure("Character not found.");

        var result = await sessionRepository.GetSessionKills(characterId, sourceName, sessionNo);
        return Result<List<LootKillEntry>>.Success(result);
    }

}
