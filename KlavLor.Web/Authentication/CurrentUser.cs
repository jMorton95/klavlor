using KlavLor.Application.Interfaces.Authentication;

namespace KlavLor.Web.Authentication;

public class CurrentUser(ISessionStateManager sessionManager) : ICurrentUser
{
    public int? UserId => sessionManager.GetUserSessionId();
    public bool IsAdmin => sessionManager.IsUserSessionAdministrator();
}
