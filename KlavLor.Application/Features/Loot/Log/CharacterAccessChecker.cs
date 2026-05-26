using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Log;

// Shared per-character visibility gate used by every loot-related handler.
// Admins see everything, owners see their own, others only see characters
// flagged IsVisible and not IsAdminHidden.
public sealed class CharacterAccessChecker(
    IGameCharacterRepository gameCharacterRepository,
    ICurrentUser currentUser)
{
    public async Task<bool> CanAccess(int characterId)
    {
        if (currentUser.IsAdmin)
            return true;

        var character = await gameCharacterRepository.GetById(characterId);
        if (character is null)
            return false;

        if (character.UserId == currentUser.UserId)
            return true;

        return character.IsVisible && !character.IsAdminHidden;
    }
}
