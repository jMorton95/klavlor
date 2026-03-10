using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Users.Search;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Specifications;
using KlavLor.Infrastructure.Persistence.EntityFramework.Shared;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Users;

internal sealed class UserQueryRepository(DataContext dataContext, ILogger<UserQueryRepository> logger) : IUserSearchRepository
{
    public async Task<PagedList<UserSearchResponse>> GetUsersBySearch(PagedQuery pagedQuery)
    {
        try
        {
            var query = dataContext.Users.AsQueryable();

            query = query.SortByProperty(pagedQuery.SortBy, pagedQuery.SortDirection);

            if (!string.IsNullOrWhiteSpace(pagedQuery.SearchTerm))
            {
                var searchStrings = pagedQuery.SearchTerm.ToLower().Split(" ");

                query = query.Where(u =>
                    searchStrings.Any(substr =>
                        u.Email.ToLower().Contains(substr)
                        || u.FirstName.ToLower().Contains(substr)
                        || u.LastName.ToLower().Contains(substr)));
            }

            var count = await query.CountAsync();

            query = query.WithPaging(pagedQuery);

            var results = await query.ProjectToDto(UserSpecifications.ToSearchResponse).ToListAsync();

            var pagination = new Pagination(count, pagedQuery.PageNumber, pagedQuery.PageSize);

            return new PagedList<UserSearchResponse>(results, pagination, pagedQuery.SortDirection, pagedQuery.SearchTerm, pagedQuery.SortBy);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search users with query {@PagedQuery}", pagedQuery);
            throw new RepositoryException("Failed to search users", ex);
        }
    }
}
