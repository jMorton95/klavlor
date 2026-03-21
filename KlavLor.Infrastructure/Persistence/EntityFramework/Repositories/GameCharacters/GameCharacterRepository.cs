using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.GameCharacters;

internal sealed class GameCharacterRepository(DataContext dataContext, ILogger<GameCharacterRepository> logger) : IGameCharacterRepository
{
    public async Task<GameCharacter?> GetById(int id)
    {
        try
        {
            return await dataContext.GameCharacters.FirstOrDefaultAsync(gc => gc.Id == id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get game character {Id}", id);
            throw new RepositoryException("Failed to get game character", ex);
        }
    }

    public async Task<GameCharacter?> GetByUserAndRuneLiteId(int userId, string runeLiteId)
    {
        try
        {
            return await dataContext.GameCharacters
                .FirstOrDefaultAsync(gc => gc.UserId == userId && gc.RuneLiteId == runeLiteId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get game character for user {UserId}, RuneLiteId {RuneLiteId}", userId, runeLiteId);
            throw new RepositoryException("Failed to get game character", ex);
        }
    }

    public async Task<List<GameCharacter>> GetByUserId(int userId)
    {
        try
        {
            return await dataContext.GameCharacters
                .Where(gc => gc.UserId == userId)
                .OrderBy(gc => gc.DisplayName ?? gc.RuneLiteId)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get game characters for user {UserId}", userId);
            throw new RepositoryException("Failed to get game characters", ex);
        }
    }

    public async Task<bool> IsDisplayNameTaken(string displayName, int? excludeCharacterId = null)
    {
        try
        {
            var query = dataContext.GameCharacters
                .Where(gc => gc.DisplayName == displayName);

            if (excludeCharacterId.HasValue)
                query = query.Where(gc => gc.Id != excludeCharacterId.Value);

            return await query.AnyAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check display name availability for {Name}", displayName);
            throw new RepositoryException("Failed to check display name", ex);
        }
    }

    public async Task<GameCharacter> Save(GameCharacter character)
    {
        try
        {
            if (character.Id == 0)
                dataContext.GameCharacters.Add(character);
            else
                dataContext.GameCharacters.Update(character);

            await dataContext.SaveChangesAsync();
            return character;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save game character {RuneLiteId}", character.RuneLiteId);
            throw new RepositoryException("Failed to save game character", ex);
        }
    }

    public async Task Delete(GameCharacter character)
    {
        try
        {
            dataContext.GameCharacters.Remove(character);
            await dataContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to delete game character {Id}", character.Id);
            throw new RepositoryException("Failed to delete game character", ex);
        }
    }

    public async Task DeleteAllForUser(int userId)
    {
        try
        {
            await dataContext.GameCharacters
                .Where(gc => gc.UserId == userId)
                .ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete all game characters for user {UserId}", userId);
            throw new RepositoryException("Failed to delete all game characters", ex);
        }
    }

    public async Task<int> GetUnassignedRecordCount(int userId)
    {
        try
        {
            return await dataContext.LootRecords
                .Where(r => r.UserId == userId && r.GameCharacterId == null)
                .CountAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to count unassigned records for user {UserId}", userId);
            throw new RepositoryException("Failed to count unassigned records", ex);
        }
    }

    public async Task<int> AssignUnassignedRecords(int userId, int gameCharacterId, string runeLiteId)
    {
        try
        {
            return await dataContext.Database.ExecuteSqlAsync(
                $"""
                UPDATE "LootRecords"
                SET "GameCharacterId" = {gameCharacterId}
                WHERE "UserId" = {userId} AND "GameCharacterId" IS NULL
                """);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to assign unassigned records for user {UserId} to character {CharacterId}", userId, gameCharacterId);
            throw new RepositoryException("Failed to assign unassigned records", ex);
        }
    }
}
