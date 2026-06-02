using KlavLor.Application.Features.Search;

namespace KlavLor.Application.Interfaces.Repositories;

// New cross-entity search queries. Templates and Users are served by the existing
// ITemplateSearchRepository / IUserSearchRepository (the SearchHandler orchestrates),
// so only the genuinely new queries live here.
public interface ISearchRepository
{
    Task<List<SearchCharacterResult>> SearchCharacters(string term, int limit);
    Task<List<SearchSourceResult>> SearchSources(string term, int limit);
    Task<List<SearchDropResult>> SearchDrops(string term, int limit);
    Task<List<SearchItemResult>> SearchItemCatalog(string term, int limit);
}
