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

}
