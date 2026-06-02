using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Web.Authentication;

/// <summary>
/// Makes long-lived cookie sessions revocable. On an interval (<see cref="AuthConstants.SessionValidationInterval"/>)
/// the authenticated principal is re-checked against the database:
/// <list type="bullet">
/// <item>a missing user, a deactivated user, or a security-stamp mismatch (deactivation, password
/// change, "sign out everywhere") rejects the session and signs it out;</item>
/// <item>otherwise the cookie's role claims are re-synced from the database, so role grants and
/// revocations take effect within one interval <em>without</em> forcing a re-login.</item>
/// </list>
/// Role revocation timing is identical to the old reject-on-stamp approach (it lands at the same
/// revalidation point) — the only difference is the user keeps their session.
/// </summary>
public sealed class SecurityStampCookieValidator(
    IUserRepository userRepository,
    TimeProvider timeProvider,
    ILogger<SecurityStampCookieValidator> logger)
{
    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal?.Identity is not { IsAuthenticated: true })
            return;

        // Throttle DB hits: skip revalidation if we checked recently within this cookie's lifetime.
        var now = timeProvider.GetUtcNow();
        var lastValidated = context.Properties.GetString(AuthConstants.LastValidatedKey);
        if (lastValidated is not null
            && DateTimeOffset.TryParse(lastValidated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var checkedAt)
            && now - checkedAt < AuthConstants.SessionValidationInterval)
        {
            return;
        }

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var stampClaim = principal.FindFirst(AuthConstants.SecurityStampClaimType)?.Value;

        if (!int.TryParse(userIdClaim, out var userId) || string.IsNullOrEmpty(stampClaim))
        {
            await RejectAsync(context);
            return;
        }

        var user = await userRepository.GetById(userId);

        if (user is null || !user.IsActive || !string.Equals(user.SecurityStamp, stampClaim, StringComparison.Ordinal))
        {
            logger.LogInformation("Rejecting stale session for user {UserId}.", userId);
            await RejectAsync(context);
            return;
        }

        // Session is valid. Re-sync role claims from the database so role grants/revocations apply
        // without a logout. (Security is unchanged: a removed role disappears from the cookie at the
        // same revalidation point that previously triggered a reject; the stamp check above still
        // hard-invalidates on deactivation / password change / "sign out everywhere".)
        var cookieRoles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        var dbRoles = user.UserRoles
            .Where(ur => ur.Role is not null)
            .Select(ur => ur.Role!.Name.ToString())
            .ToHashSet(StringComparer.Ordinal);

        if (!cookieRoles.SetEquals(dbRoles))
        {
            logger.LogInformation("Re-syncing role claims for user {UserId}.", userId);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(AuthConstants.SecurityStampClaimType, user.SecurityStamp),
            };
            claims.AddRange(dbRoles.Select(role => new Claim(ClaimTypes.Role, role)));

            context.ReplacePrincipal(new ClaimsPrincipal(
                new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        }

        // Record the check and persist the (possibly refreshed) principal back into the cookie.
        context.Properties.SetString(AuthConstants.LastValidatedKey, now.ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
