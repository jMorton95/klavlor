namespace KlavLor.Application.Common;

public record PagedQuery
(
    int PageSize = 10,
    int PageNumber = 1,
    string? SortBy = null,
    string? SearchTerm = null,
    SortDirection SortDirection = SortDirection.Ascending
);
