using System.Linq.Expressions;
using KlavLor.Application.Features.Users.Search;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Specifications;

public static class UserSpecifications
{
    public static Expression<Func<User, UserSearchResponse>> ToSearchResponse =>
        user => new UserSearchResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.IsActive,
            user.IsLockedOut,
            user.UserRoles.Select(ur => ur.Role!.Name.ToString()).ToArray()
        );
}
