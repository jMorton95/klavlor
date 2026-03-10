using KlavLor.Application.Common;
using KlavLor.Application.Features.Users.Search;

namespace KlavLor.Application.Interfaces.Repositories;

public interface IUserSearchRepository
{
    Task<PagedList<UserSearchResponse>> GetUsersBySearch(PagedQuery pagedQuery);
}
