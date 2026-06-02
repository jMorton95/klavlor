using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;

namespace KlavLor.Web.Authentication;

public class CurrentUser(ISessionStateManager sessionManager) : ICurrentUser
{
    public int? UserId => sessionManager.GetUserSessionId();
    public bool IsAdmin => sessionManager.IsUserSessionAdministrator();
    public bool IsInRole(RoleName roleName) => sessionManager.IsInRole(roleName);
}
