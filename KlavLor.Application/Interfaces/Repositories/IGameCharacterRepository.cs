using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Repositories;

public interface IGameCharacterRepository
{
    Task<GameCharacter?> GetById(int id);

    // Visible characters an admin can target (e.g. for special-loot injection), ordered by name.
    Task<List<GameCharacter>> GetSelectable();
    Task<GameCharacter?> GetByUserAndRuneLiteId(int userId, string runeLiteId);
    Task<List<GameCharacter>> GetByUserId(int userId);
    Task<Dictionary<int, (int Sources, long Kills, long Value)>> GetCharacterStats(int userId);
    Task<bool> IsDisplayNameTaken(string displayName, int? excludeCharacterId = null);
    Task<GameCharacter> Save(GameCharacter character);
    Task Delete(GameCharacter character);
    Task DeleteAllForUser(int userId);
    Task<int> GetUnassignedRecordCount(int userId);
    Task<int> AssignUnassignedRecords(int userId, int gameCharacterId, string runeLiteId);
}
