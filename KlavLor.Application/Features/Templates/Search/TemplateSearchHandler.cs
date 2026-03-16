using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Templates.Search;

public sealed class TemplateSearchHandler(
    ITemplateSearchRepository templateSearchRepository,
    TemplateSearchValidator validator,
    ICurrentUser currentUser)
{
    public async Task<Result<PagedList<TemplateSearchResponse>>> Handle(TemplateSearchQuery query)
    {
        var validationResult = await validator.ValidateAsync(query);

        if (!validationResult.IsValid)
            return Result<PagedList<TemplateSearchResponse>>.ValidationFailure(validationResult.ToDictionary());

        var results = await templateSearchRepository.GetTemplatesBySearch(currentUser.UserId, query, currentUser.IsAdmin);

        return Result<PagedList<TemplateSearchResponse>>.Success(results);
    }
}
