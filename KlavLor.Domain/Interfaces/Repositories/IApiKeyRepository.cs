using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface IApiKeyRepository
{
    Task<ApiKey?> GetByKeyHash(string keyHash);
    Task<List<ApiKey>> GetByUserId(int userId);
    Task<List<ApiKey>> GetAll();
    Task<ApiKey?> GetById(int id);
    Task<bool> Save(ApiKey apiKey);
    Task<int> Delete(int id);
    Task UpdateLastUsedAt(int id, DateTimeOffset timestamp);
    Task DeactivateAllForUser(int userId);
}
