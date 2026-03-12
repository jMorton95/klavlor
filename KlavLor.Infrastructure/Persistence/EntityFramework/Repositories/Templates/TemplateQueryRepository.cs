using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Templates.Search;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Infrastructure.Persistence.EntityFramework.Shared;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Templates;

internal sealed class TemplateQueryRepository(DataContext dataContext, ILogger<TemplateQueryRepository> logger) : ITemplateSearchRepository
{
    public async Task<PagedList<TemplateSearchResponse>> GetTemplatesBySearch(int? userId, PagedQuery pagedQuery)
    {
        try
        {
            var query = dataContext.Templates
                .Where(t => (userId.HasValue && t.CreatedById == userId.Value) || t.IsPublic)
                .AsQueryable();

            query = query.OrderByDescending(t => userId.HasValue && t.CreatedById == userId.Value)
                .ThenByDescending(t => t.SavedAt);

            if (!string.IsNullOrWhiteSpace(pagedQuery.SearchTerm))
            {
                var searchTerm = pagedQuery.SearchTerm.ToLower();

                query = query.Where(t =>
                    t.Name.ToLower().Contains(searchTerm)
                    || (t.Description != null && t.Description.ToLower().Contains(searchTerm)));
            }

            var count = await query.CountAsync();

            query = query.WithPaging(pagedQuery);

            var results = await query.Select(t => new TemplateSearchResponse(
                t.Id,
                t.Name,
                t.Description,
                t.IsPublic,
                t.Nodes.Count,
                t.SavedAt,
                (t.CreatedBy != null ? t.CreatedBy.FirstName + " " + t.CreatedBy.LastName : "Unknown"),
                userId.HasValue && t.CreatedById == userId.Value
            )).ToListAsync();

            var pagination = new Pagination(count, pagedQuery.PageNumber, pagedQuery.PageSize);

            return new PagedList<TemplateSearchResponse>(results, pagination, pagedQuery.SortDirection, pagedQuery.SearchTerm, pagedQuery.SortBy);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search templates with query {@PagedQuery}", pagedQuery);
            throw new RepositoryException("Failed to search templates", ex);
        }
    }

    public async Task<List<TemplateCloneOption>> GetCloneOptions(int userId)
    {
        try
        {
            return await dataContext.Templates
                .Where(t => t.CreatedById == userId || t.IsPublic)
                .OrderBy(t => t.Name)
                .Select(t => new TemplateCloneOption(
                    t.Id,
                    t.Name,
                    t.CreatedBy != null ? t.CreatedBy.FirstName + " " + t.CreatedBy.LastName : "Unknown",
                    t.CreatedById == userId
                ))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get clone options");
            throw new RepositoryException("Failed to get clone options", ex);
        }
    }
}
