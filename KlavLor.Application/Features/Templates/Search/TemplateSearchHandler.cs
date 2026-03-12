using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Templates.Search;

public sealed class TemplateSearchHandler(
    ITemplateSearchRepository templateSearchRepository,
    TemplateSearchValidator validator)
{
    public async Task<Result<PagedList<TemplateSearchResponse>>> Handle(TemplateSearchQuery query, int? userId, bool isAdmin = false)
    {
        var validationResult = await validator.ValidateAsync(query);

        if (!validationResult.IsValid)
            return Result<PagedList<TemplateSearchResponse>>.ValidationFailure(validationResult.ToDictionary());

        var results = await templateSearchRepository.GetTemplatesBySearch(userId, query, isAdmin);

        return Result<PagedList<TemplateSearchResponse>>.Success(results);
    }
}
