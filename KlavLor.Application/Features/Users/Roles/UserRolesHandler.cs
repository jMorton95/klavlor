using KlavLor.Application.Common;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Domain.Shared;

namespace KlavLor.Application.Features.Users.Roles;

public sealed class UserRolesHandler(IUserRepository userRepository, IRoleRepository roleRepository)
{
    // Roles an admin may grant/revoke from the UI. Admin is intentionally excluded — it is never
    // assignable through user management.
    public static readonly IReadOnlyList<RoleName> AssignableRoles =
        Enum.GetValues<RoleName>().Where(r => r != RoleName.Admin).ToArray();

    public async Task<Result<UserRolesResponse>> Handle(int userId)
    {
        var user = await userRepository.GetById(userId);
        return user is null
            ? Result<UserRolesResponse>.Failure("User not found.")
            : Result<UserRolesResponse>.Success(ToResponse(user));
    }

    public async Task<Result<UserRolesResponse>> Toggle(int userId, RoleName roleName)
    {
        if (roleName == RoleName.Admin)
            return Result<UserRolesResponse>.Failure("The Admin role cannot be assigned here.");

        var user = await userRepository.GetById(userId);
        if (user is null)
            return Result<UserRolesResponse>.Failure("User not found.");

        var role = await roleRepository.GetByName(roleName);
        if (role is null)
            return Result<UserRolesResponse>.Failure("Role not found.");

        // AssignRole/UnassignRole bump the security stamp, invalidating the user's active sessions.
        if (user.UserRoles.Any(ur => ur.RoleId == role.Id))
            user.UnassignRole(role);
        else
            user.AssignRole(role);

        await userRepository.SaveUser(user);
        return Result<UserRolesResponse>.Success(ToResponse(user));
    }

    private static UserRolesResponse ToResponse(User user) =>
        new(user.Id, user.UserRoles.Where(ur => ur.Role is not null).Select(ur => ur.Role!.Name).ToHashSet());
}
