using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Users;

public static class UserMapper
{
    public static UserResponse MapToResponse(this User user)
    {
        return new UserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.IsActive,
            user.UserRoles.Select(ur => ur.Role?.Name.ToString() ?? "").ToArray()
        );
    }
}
