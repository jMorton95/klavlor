using KlavLor.Domain.Shared;

namespace KlavLor.Application.Interfaces.Authentication;

public interface ICurrentUser
{
    int? UserId { get; }
    bool IsAdmin { get; }
    bool IsInRole(RoleName roleName);
}
