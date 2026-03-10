using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;

namespace KlavLor.Web.Authentication;

public class AspNetSessionStateManager(IHttpContextAccessor httpContextAccessor, ILogger<AspNetSessionStateManager> logger, TimeProvider timeProvider) : ISessionStateManager
{
    private HttpContext HttpContext => httpContextAccessor.HttpContext!;

    public int? GetUserSessionId()
    {
        if (HttpContext?.User is null) return null;

        var userIdClaim = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (int.TryParse(userIdClaim, out var userId))
            return userId;

        return null;
    }

    public async Task LoginAsync(int userId, string[] roleNames, bool persistSession = false)
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId.ToString())];

        claims.AddRange(roleNames.Select(userRole => new Claim(ClaimTypes.Role, userRole)));

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = persistSession,
            ExpiresUtc = persistSession ? timeProvider.GetUtcNow().Add(TimeSpan.FromHours(3)) : null,
            IssuedUtc = timeProvider.GetUtcNow(),
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);

        logger.LogInformation("User {UserId} logged in. Persistent Session: {PersistentSession}", userId, persistSession.ToString());
    }

    public async Task LogoutAsync()
    {
        var userId = GetUserSessionId();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        logger.LogInformation("User {userId} logged out.", userId);
    }

    public bool IsAuthenticated() => GetUserSessionId() != null;

    public bool IsUserSessionAdministrator() => HasSpecificRole(RoleName.Admin);

    private bool HasSpecificRole(RoleName roleName)
    {
        if (!Enum.IsDefined(roleName))
            throw new InvalidEnumArgumentException(nameof(roleName), (int)roleName, typeof(RoleName));

        var userId = GetUserSessionId();

        if (userId is null)
            return false;

        var hasSpecificRole = HttpContext.User.Claims.Any(
            c => c.Type == ClaimTypes.Role && c.Value == roleName.ToString());

        return hasSpecificRole;
    }
}
