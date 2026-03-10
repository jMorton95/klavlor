namespace KlavLor.Application.Common;

public sealed record Pagination(int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record PagedList<T>
(
    List<T> Items,
    Pagination Pagination,
    SortDirection SortDirection = SortDirection.Ascending,
    string? SearchTerm = null,
    string? SortBy = null
);
