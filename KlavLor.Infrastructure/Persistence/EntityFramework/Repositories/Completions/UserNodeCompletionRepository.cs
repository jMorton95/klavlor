using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Completions;

internal sealed class UserNodeCompletionRepository(DataContext dataContext, ILogger<UserNodeCompletionRepository> logger) : IUserNodeCompletionRepository
{
    public async Task<List<UserNodeCompletion>> GetByUserAndTemplate(int userId, int templateId)
    {
        try
        {
            return await dataContext.UserNodeCompletions
                .Where(c => c.UserId == userId
                    && dataContext.TemplateNodes
                        .Where(n => n.TemplateId == templateId)
                        .Select(n => n.Id)
                        .Contains(c.TemplateNodeId))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get completions for user {UserId} and template {TemplateId}", userId, templateId);
            throw new RepositoryException("Failed to get completions", ex);
        }
    }

    public async Task<bool> Toggle(int userId, int templateNodeId, string? note = null)
    {
        try
        {
            var existing = await dataContext.UserNodeCompletions
                .FirstOrDefaultAsync(c => c.UserId == userId && c.TemplateNodeId == templateNodeId);

            if (existing is not null)
            {
                dataContext.UserNodeCompletions.Remove(existing);
            }
            else
            {
                dataContext.UserNodeCompletions.Add(new UserNodeCompletion
                {
                    UserId = userId,
                    TemplateNodeId = templateNodeId,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Note = note
                });
            }

            await dataContext.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to toggle completion for user {UserId} and node {NodeId}", userId, templateNodeId);
            throw new RepositoryException("Failed to toggle completion", ex);
        }
    }

    public async Task<UserNodeCompletion?> GetCompletion(int userId, int templateNodeId)
    {
        return await dataContext.UserNodeCompletions
            .FirstOrDefaultAsync(c => c.UserId == userId && c.TemplateNodeId == templateNodeId);
    }

    public async Task<bool> IsCompleted(int userId, int templateNodeId)
    {
        return await dataContext.UserNodeCompletions
            .AnyAsync(c => c.UserId == userId && c.TemplateNodeId == templateNodeId);
    }

    public async Task<IReadOnlyList<AutoCompletableNode>> GetAutoCompletableNodes(int userId, IReadOnlyCollection<string> itemNames)
    {
        if (itemNames.Count == 0)
            return [];

        try
        {
            var lowered = itemNames.Select(n => n.ToLower()).Distinct().ToList();

            var rows = await (
                from node in dataContext.TemplateNodes
                join template in dataContext.Templates on node.TemplateId equals template.Id
                join gear in dataContext.GearItems on node.GearItemId equals gear.Id
                where template.CreatedById == userId
                      && lowered.Contains(gear.Name.ToLower())
                      && !dataContext.UserNodeCompletions.Any(c => c.UserId == userId && c.TemplateNodeId == node.Id)
                select new { node.Id, gear.Name }
            ).ToListAsync();

            return rows.Select(r => new AutoCompletableNode(r.Id, r.Name)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to find auto-completable nodes for user {UserId}", userId);
            throw new RepositoryException("Failed to find auto-completable nodes", ex);
        }
    }

    public async Task AddCompletions(IReadOnlyCollection<UserNodeCompletion> completions)
    {
        if (completions.Count == 0)
            return;

        try
        {
            // Defensive: drop any (user, node) pairs that already exist so a concurrent manual
            // completion can't cause a duplicate-key insert.
            var userIds = completions.Select(c => c.UserId).Distinct().ToList();
            var nodeIds = completions.Select(c => c.TemplateNodeId).Distinct().ToList();
            var existing = await dataContext.UserNodeCompletions
                .Where(c => userIds.Contains(c.UserId) && nodeIds.Contains(c.TemplateNodeId))
                .Select(c => new { c.UserId, c.TemplateNodeId })
                .ToListAsync();
            var existingKeys = existing.Select(e => (e.UserId, e.TemplateNodeId)).ToHashSet();

            var fresh = completions
                .Where(c => !existingKeys.Contains((c.UserId, c.TemplateNodeId)))
                .ToList();
            if (fresh.Count == 0)
                return;

            dataContext.UserNodeCompletions.AddRange(fresh);
            await dataContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add {Count} auto-completions", completions.Count);
            throw new RepositoryException("Failed to add completions", ex);
        }
    }
}
