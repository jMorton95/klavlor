using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

// Progression completion is entirely manual: the only way a node gets ticked is a user
// clicking it in the viewer (Toggle). Loot ingest deliberately does NOT complete nodes —
// the drop-driven auto-completion feature (and its generated notes) was removed. Do not
// reintroduce a write path here that is driven by anything other than an explicit user action.
public interface IUserNodeCompletionRepository
{
    Task<List<UserNodeCompletion>> GetByUserAndTemplate(int userId, int templateId);
    Task<bool> Toggle(int userId, int templateNodeId, string? note = null);
    Task<UserNodeCompletion?> GetCompletion(int userId, int templateNodeId);
    Task<bool> IsCompleted(int userId, int templateNodeId);
}
