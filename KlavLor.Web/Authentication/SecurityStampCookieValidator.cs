using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Web.Authentication;

/// <summary>
/// Makes long-lived cookie sessions revocable. On an interval (<see cref="AuthConstants.SessionValidationInterval"/>)
/// the authenticated principal is re-checked against the database: a missing user, a deactivated user, or a
/// security-stamp mismatch causes the session to be rejected and signed out. Bumping a user's
/// <c>SecurityStamp</c> (deactivation, role change, password reset, "sign out everywhere") therefore
/// invalidates all of their outstanding cookies within one interval.
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

        // Record the check and persist the updated property back into the cookie.
        context.Properties.SetString(AuthConstants.LastValidatedKey, now.ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
