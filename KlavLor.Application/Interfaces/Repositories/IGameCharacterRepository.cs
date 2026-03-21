using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Repositories;

public interface IGameCharacterRepository
{
    Task<GameCharacter?> GetById(int id);
    Task<GameCharacter?> GetByUserAndRuneLiteId(int userId, string runeLiteId);
    Task<List<GameCharacter>> GetByUserId(int userId);
    Task<bool> IsDisplayNameTaken(string displayName, int? excludeCharacterId = null);
    Task<GameCharacter> Save(GameCharacter character);
    Task Delete(GameCharacter character);
    Task DeleteAllForUser(int userId);
    Task<int> AssignUnassignedRecords(int userId, int gameCharacterId, string runeLiteId);
}
