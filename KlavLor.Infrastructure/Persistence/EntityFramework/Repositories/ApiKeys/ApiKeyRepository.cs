using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.ApiKeys;

internal sealed class ApiKeyRepository(DataContext dataContext, ILogger<ApiKeyRepository> logger) : IApiKeyRepository
{
    public async Task<ApiKey?> GetByKeyHash(string keyHash)
    {
        try
        {
            return await dataContext.ApiKeys
                .Include(k => k.User)
                .SingleOrDefaultAsync(k => k.KeyHash == keyHash && k.IsActive);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get API key by hash");
            throw new RepositoryException("Failed to get API key by hash", ex);
        }
    }

    public async Task<List<ApiKey>> GetByUserId(int userId)
    {
        try
        {
            return await dataContext.ApiKeys
                .Include(k => k.User)
                .Where(k => k.UserId == userId)
                .OrderByDescending(k => k.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get API keys for user {UserId}", userId);
            throw new RepositoryException("Failed to get API keys for user", ex);
        }
    }

    public async Task<List<ApiKey>> GetAll()
    {
        try
        {
            return await dataContext.ApiKeys
                .Include(k => k.User)
                .OrderByDescending(k => k.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get all API keys");
            throw new RepositoryException("Failed to get all API keys", ex);
        }
    }

    public async Task<ApiKey?> GetById(int id)
    {
        try
        {
            return await dataContext.ApiKeys
                .Include(k => k.User)
                .FirstOrDefaultAsync(k => k.Id == id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get API key {Id}", id);
            throw new RepositoryException("Failed to get API key", ex);
        }
    }

    public async Task<bool> Save(ApiKey apiKey)
    {
        try
        {
            if (apiKey.Id == 0)
                dataContext.ApiKeys.Add(apiKey);
            else
                dataContext.ApiKeys.Update(apiKey);

            return await dataContext.SaveChangesAsync() > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save API key {Id}", apiKey.Id);
            throw new RepositoryException("Failed to save API key", ex);
        }
    }

    public async Task<int> Delete(int id)
    {
        try
        {
            return await dataContext.ApiKeys.Where(k => k.Id == id).ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete API key {Id}", id);
            throw new RepositoryException("Failed to delete API key", ex);
        }
    }

    public async Task DeactivateAllForUser(int userId)
    {
        try
        {
            await dataContext.ApiKeys
                .Where(k => k.UserId == userId && k.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.IsActive, false));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deactivate API keys for user {UserId}", userId);
            throw new RepositoryException("Failed to deactivate API keys for user", ex);
        }
    }

    public async Task UpdateLastUsedAt(int id, DateTimeOffset timestamp)
    {
        try
        {
            await dataContext.ApiKeys
                .Where(k => k.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, timestamp));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update LastUsedAt for API key {Id}", id);
        }
    }
}
