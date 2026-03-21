using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Characters;

public sealed class CharacterHandler(
    IGameCharacterRepository characterRepository,
    ILootLogRepository lootLogRepository,
    ICurrentUser currentUser)
{
    public async Task<Result<List<CharacterSummary>>> HandleList()
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result<List<CharacterSummary>>.Failure("Not authenticated.");

        var characters = await characterRepository.GetByUserId(userId.Value);
        var summaries = characters.Select(c => new CharacterSummary(
            c.Id, c.RuneLiteId, c.DisplayName, c.IsVisible, c.IsAdminHidden)).ToList();

        return Result<List<CharacterSummary>>.Success(summaries);
    }

    public async Task<Result<List<CharacterSummary>>> HandleListForUser(int userId)
    {
        var characters = await characterRepository.GetByUserId(userId);
        var summaries = characters.Select(c => new CharacterSummary(
            c.Id, c.RuneLiteId, c.DisplayName, c.IsVisible, c.IsAdminHidden)).ToList();

        return Result<List<CharacterSummary>>.Success(summaries);
    }

    public async Task<Result> HandleUpdateName(int characterId, string? displayName)
    {
        var character = await characterRepository.GetById(characterId);
        if (character is null)
            return Result.Failure("Character not found.");

        if (!currentUser.IsAdmin && character.UserId != currentUser.UserId)
            return Result.Failure("Not authorized.");

        var trimmed = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

        if (trimmed is not null)
        {
            var taken = await characterRepository.IsDisplayNameTaken(trimmed, characterId);
            if (taken)
                return Result.Failure("That display name is already in use.");
        }

        character.DisplayName = trimmed;
        await characterRepository.Save(character);
        return Result.Success();
    }

    public async Task<Result> HandleToggleVisibility(int characterId)
    {
        var character = await characterRepository.GetById(characterId);
        if (character is null)
            return Result.Failure("Character not found.");

        if (!currentUser.IsAdmin && character.UserId != currentUser.UserId)
            return Result.Failure("Not authorized.");

        character.IsVisible = !character.IsVisible;
        await characterRepository.Save(character);
        return Result.Success();
    }

    public async Task<Result> HandleToggleAdminHidden(int characterId)
    {
        if (!currentUser.IsAdmin)
            return Result.Failure("Not authorized.");

        var character = await characterRepository.GetById(characterId);
        if (character is null)
            return Result.Failure("Character not found.");

        character.IsAdminHidden = !character.IsAdminHidden;
        await characterRepository.Save(character);
        return Result.Success();
    }

    public async Task<Result> HandleDeleteCharacterData(int characterId)
    {
        if (!currentUser.IsAdmin)
            return Result.Failure("Not authorized.");

        var character = await characterRepository.GetById(characterId);
        if (character is null)
            return Result.Failure("Character not found.");

        await lootLogRepository.DeleteAllForCharacter(characterId);
        await characterRepository.Delete(character);
        return Result.Success();
    }

    public async Task<Result> HandleDeleteAllUserData(int userId)
    {
        if (!currentUser.IsAdmin)
            return Result.Failure("Not authorized.");

        await lootLogRepository.DeleteAllForUser(userId);
        await characterRepository.DeleteAllForUser(userId);
        return Result.Success();
    }

    public async Task<Result> HandleAssignUnassigned(int characterId)
    {
        var character = await characterRepository.GetById(characterId);
        if (character is null)
            return Result.Failure("Character not found.");

        if (!currentUser.IsAdmin && character.UserId != currentUser.UserId)
            return Result.Failure("Not authorized.");

        await characterRepository.AssignUnassignedRecords(character.UserId, character.Id, character.RuneLiteId);
        return Result.Success();
    }
}

public sealed record CharacterSummary(
    int Id,
    string RuneLiteId,
    string? DisplayName,
    bool IsVisible,
    bool IsAdminHidden);
