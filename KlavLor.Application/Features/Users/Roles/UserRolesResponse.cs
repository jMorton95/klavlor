using KlavLor.Domain.Shared;

namespace KlavLor.Application.Features.Users.Roles;

public sealed record UserRolesResponse(int UserId, IReadOnlySet<RoleName> AssignedRoles);
