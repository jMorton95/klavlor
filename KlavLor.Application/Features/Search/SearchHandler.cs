using KlavLor.Application.Common;
using KlavLor.Application.Features.Templates.Search;
using KlavLor.Application.Features.Users.Search;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Search;

// Orchestrates the database-search sections. Each section endpoint calls exactly one
// of these methods on its own request/scope, so no two queries ever share the (non
// thread-safe) DbContext. Templates/Users reuse the existing search repositories to
// inherit their ownership/visibility rules rather than duplicating them.
public sealed class SearchHandler(
    ISearchRepository searchRepository,
    ITemplateSearchRepository templateSearchRepository,
    IUserSearchRepository userSearchRepository,
    ICurrentUser currentUser)
{
    // Below this length every section is a no-op — avoids unindexed '%a%' scans.
    public const int MinTermLength = 2;

    private const int CharacterLimit = 6;
    private const int SourceLimit = 8;
    private const int DropLimit = 8;
    private const int ItemLimit = 6;
    private const int TemplateLimit = 6;
    private const int UserLimit = 6;

    public static bool IsSearchable(string? term) =>
        !string.IsNullOrWhiteSpace(term) && term.Trim().Length >= MinTermLength;

    public async Task<List<SearchCharacterResult>> SearchCharacters(string? term)
    {
        if (!IsSearchable(term)) return [];
        return await searchRepository.SearchCharacters(term!.Trim(), CharacterLimit);
    }

    public async Task<List<SearchSourceResult>> SearchSources(string? term)
    {
        if (!IsSearchable(term)) return [];
        return await searchRepository.SearchSources(term!.Trim(), SourceLimit);
    }

    public async Task<List<SearchDropResult>> SearchDrops(string? term)
    {
        if (!IsSearchable(term)) return [];
        return await searchRepository.SearchDrops(term!.Trim(), DropLimit);
    }

    public async Task<List<SearchItemResult>> SearchItemCatalog(string? term)
    {
        if (!IsSearchable(term)) return [];
        return await searchRepository.SearchItemCatalog(term!.Trim(), ItemLimit);
    }

    public async Task<List<TemplateSearchResponse>> SearchTemplates(string? term)
    {
        if (!IsSearchable(term)) return [];

        var paged = new PagedQuery(PageSize: TemplateLimit, PageNumber: 1, SearchTerm: term!.Trim());
        var result = await templateSearchRepository.GetTemplatesBySearch(currentUser.UserId, paged, currentUser.IsAdmin);
        return result.Items;
    }

    public async Task<List<UserSearchResponse>> SearchUsers(string? term)
    {
        // Admin-only section — silently empty for everyone else.
        if (!currentUser.IsAdmin || !IsSearchable(term)) return [];

        var paged = new PagedQuery(PageSize: UserLimit, PageNumber: 1, SearchTerm: term!.Trim());
        var result = await userSearchRepository.GetUsersBySearch(paged);
        return result.Items;
    }
}
