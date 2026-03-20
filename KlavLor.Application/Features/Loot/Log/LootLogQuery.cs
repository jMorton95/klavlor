using KlavLor.Application.Common;

namespace KlavLor.Application.Features.Loot.Log;

public record LootLogQuery(
    int PageSize = 20,
    int PageNumber = 1,
    string? SortBy = null,
    string? SearchTerm = null,
    SortDirection SortDirection = SortDirection.Descending
) : PagedQuery(PageSize, PageNumber, SortBy, SearchTerm, SortDirection);
