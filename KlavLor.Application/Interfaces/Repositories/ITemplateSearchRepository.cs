using KlavLor.Application.Common;
using KlavLor.Application.Features.Templates.Search;

namespace KlavLor.Application.Interfaces.Repositories;

public interface ITemplateSearchRepository
{
    Task<PagedList<TemplateSearchResponse>> GetTemplatesBySearch(int userId, PagedQuery pagedQuery);
    Task<List<TemplateCloneOption>> GetCloneOptions(int userId);
}
