namespace KlavLor.Web.Authentication;

public static class AuthConstants
{
    /// <summary>
    /// Claim type holding the user's security stamp at the time the cookie was issued.
    /// Re-checked against the database by <see cref="SecurityStampCookieValidator"/>.
    /// </summary>
    public const string SecurityStampClaimType = "security_stamp";

    /// <summary>Auth-property key recording when the principal was last revalidated against the DB.</summary>
    public const string LastValidatedKey = "security_stamp_validated_at";

    /// <summary>How often an active session is revalidated against the database.</summary>
    public static readonly TimeSpan SessionValidationInterval = TimeSpan.FromMinutes(15);

    /// <summary>Lifetime / sliding window for authentication sessions.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(180);
}
