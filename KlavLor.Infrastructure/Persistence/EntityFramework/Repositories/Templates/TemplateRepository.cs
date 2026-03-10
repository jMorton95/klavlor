using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Templates;

internal sealed class TemplateRepository(DataContext dataContext, ILogger<TemplateRepository> logger) : ITemplateRepository
{
    public async Task<Template?> GetById(int id)
    {
        try
        {
            return await dataContext.Templates
                .Include(t => t.Nodes)
                .Include(t => t.Edges)
                .Include(t => t.Groups)
                .Include(t => t.CreatedBy)
                .AsSplitQuery()
                .FirstOrDefaultAsync(t => t.Id == id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get template by id {TemplateId}", id);
            throw new RepositoryException("Failed to get template", ex);
        }
    }

    public async Task<Template?> GetByShareToken(string shareToken)
    {
        try
        {
            return await dataContext.Templates
                .Include(t => t.Nodes)
                .Include(t => t.Edges)
                .Include(t => t.Groups)
                .Include(t => t.CreatedBy)
                .AsSplitQuery()
                .FirstOrDefaultAsync(t => t.ShareToken == shareToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get template by share token");
            throw new RepositoryException("Failed to get template by share token", ex);
        }
    }

    public async Task<bool> SaveTemplate(Template template)
    {
        try
        {
            if (template.Id == 0)
                dataContext.Templates.Add(template);
            else
                dataContext.Templates.Update(template);

            return await dataContext.SaveChangesAsync() > 0;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save template");
            throw new RepositoryException("Failed to save template", ex);
        }
    }

    public async Task<int> DeleteTemplate(int id)
    {
        try
        {
            return await dataContext.Templates.Where(t => t.Id == id).ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete template {TemplateId}", id);
            throw new RepositoryException("Failed to delete template", ex);
        }
    }

    public async Task<int?> GetTemplateOwnerId(int templateId)
    {
        return await dataContext.Templates
            .Where(t => t.Id == templateId)
            .Select(t => (int?)t.CreatedById)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> UpdateNodePosition(int nodeId, double positionX, double positionY)
    {
        var rows = await dataContext.TemplateNodes
            .Where(n => n.Id == nodeId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.PositionX, positionX)
                .SetProperty(n => n.PositionY, positionY));
        return rows > 0;
    }

    public async Task<bool> UpdateGroupPosition(int groupId, double positionX, double positionY)
    {
        var rows = await dataContext.TemplateNodeGroups
            .Where(g => g.Id == groupId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.PositionX, positionX)
                .SetProperty(g => g.PositionY, positionY));
        return rows > 0;
    }

    public async Task<bool> NodeBelongsToTemplate(int nodeId, int templateId)
    {
        return await dataContext.TemplateNodes
            .AnyAsync(n => n.Id == nodeId && n.TemplateId == templateId);
    }
}
