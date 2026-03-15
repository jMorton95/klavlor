using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface IUserNodeCompletionRepository
{
    Task<List<UserNodeCompletion>> GetByUserAndTemplate(int userId, int templateId);
    Task<bool> Toggle(int userId, int templateNodeId, string? note = null);
    Task<UserNodeCompletion?> GetCompletion(int userId, int templateNodeId);
    Task<bool> IsCompleted(int userId, int templateNodeId);
}
