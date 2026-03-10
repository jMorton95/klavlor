using KlavLor.Application.Common;

namespace KlavLor.Application.Features.Users.Search;

public sealed record UserSearchQuery(
    int PageSize = 10,
    int PageNumber = 1,
    string? SortBy = null,
    string? SearchTerm = null,
    SortDirection SortDirection = SortDirection.Ascending
) : PagedQuery(PageSize, PageNumber, SortBy, SearchTerm, SortDirection);
