using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Users.Search;

public sealed class UserSearchHandler(
    IUserSearchRepository userSearchRepository,
    UserSearchValidator validator)
{
    public async Task<Result<PagedList<UserSearchResponse>>> Handle(UserSearchQuery query)
    {
        var validationResult = await validator.ValidateAsync(query);

        if (!validationResult.IsValid)
            return Result<PagedList<UserSearchResponse>>.ValidationFailure(validationResult.ToDictionary());

        var results = await userSearchRepository.GetUsersBySearch(query);

        return Result<PagedList<UserSearchResponse>>.Success(results);
    }
}
