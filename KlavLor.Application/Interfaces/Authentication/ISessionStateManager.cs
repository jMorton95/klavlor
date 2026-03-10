namespace KlavLor.Application.Interfaces.Authentication;

public interface ISessionStateManager
{
    int? GetUserSessionId();
    Task LoginAsync(int userId, string[] roleNames, bool persistSession = false);
    Task LogoutAsync();
    bool IsAuthenticated();
    bool IsUserSessionAdministrator();
}
