using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

/// <summary>A still-incomplete template node whose gear item matches a dropped item name.</summary>
public sealed record AutoCompletableNode(int NodeId, string ItemName);

public interface IUserNodeCompletionRepository
{
    Task<List<UserNodeCompletion>> GetByUserAndTemplate(int userId, int templateId);
    Task<bool> Toggle(int userId, int templateNodeId, string? note = null);
    Task<UserNodeCompletion?> GetCompletion(int userId, int templateNodeId);
    Task<bool> IsCompleted(int userId, int templateNodeId);

    /// <summary>
    /// Nodes the user could still complete via a drop: gear-item nodes in templates the user
    /// owns, whose <see cref="GearItem.Name"/> matches one of <paramref name="itemNames"/>
    /// (case-insensitive) and which the user has not already completed. Drives loot auto-completion.
    /// </summary>
    Task<IReadOnlyList<AutoCompletableNode>> GetAutoCompletableNodes(int userId, IReadOnlyCollection<string> itemNames);

    /// <summary>Bulk-inserts completions, skipping any (user, node) pairs that already exist.</summary>
    Task AddCompletions(IReadOnlyCollection<UserNodeCompletion> completions);
}
