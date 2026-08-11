using System.Security.Claims;
using System.Threading.RateLimiting;

namespace KlavLor.Web.Configuration;

/// <summary>
/// Every rate-limiting policy in the app, in one place.
///
/// The organising idea is that WHO a request comes from decides both the budget and the bucket it
/// is counted in. A signed-in user is identified and accountable — we can see exactly what they did
/// and revoke them — so they get a generous budget counted against their own account. Anonymous
/// traffic is none of those things, can only be identified by an IP that may be shared or spoofed,
/// and so gets a tighter budget counted per address.
///
/// This was previously one flat per-IP limit for all read traffic. That punished the wrong people:
/// several signed-in users behind one office or household NAT shared a single 120-per-minute
/// budget between them, and a single HTMX-heavy page can spend a dozen of those on one interaction,
/// so an ordinary session could 429 itself while an abusive anonymous client on its own address was
/// entirely unaffected.
/// </summary>
public static class ConfigureRateLimiting
{
    /// <summary>Reads by a signed-in user, counted per account. Deliberately generous: reads are
    /// cheap, HTMX fires several per interaction, and the account is attributable if abused.</summary>
    private const int AuthenticatedReadPerMinute = 600;

    /// <summary>Reads by an anonymous visitor, counted per address. The public surface — feed,
    /// character logs, source pages — has to stay usable for a signed-out visitor while giving a
    /// scraper nothing worth having.</summary>
    private const int AnonymousReadPerMinute = 120;

    public static IServiceCollection AddApplicationRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Per-IP: login attempts. Always by address — there is no user yet, and that is the
            // whole point of the limit.
            options.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    IpKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));

            // Per-user: standard mutations (node/edge/group CRUD, template CRUD, completion).
            options.AddPolicy("mutation", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    CallerKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));

            // Per-user: high-frequency position updates during drag.
            options.AddPolicy("position", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    CallerKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));

            // Per-user: loot ingestion from the RuneLite plugin.
            options.AddPolicy("loot-ingest", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    CallerKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));

            // Read endpoints, tiered by who is asking. One policy rather than two because the
            // public read surface serves both: the same route is hit by a signed-out visitor and by
            // a logged-in user, and only the request can say which — a call site cannot choose in
            // advance.
            options.AddPolicy("read", context => context.User.Identity?.IsAuthenticated == true
                ? RateLimitPartition.GetFixedWindowLimiter(
                    CallerKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = AuthenticatedReadPerMinute, Window = TimeSpan.FromMinutes(1) })
                : RateLimitPartition.GetFixedWindowLimiter(
                    IpKey(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = AnonymousReadPerMinute, Window = TimeSpan.FromMinutes(1) }));

            // Per-IP: SSE feed streams. These are long-lived connections, so the meaningful limit is
            // how many a single IP may hold open at once, not requests per minute — a window limiter
            // would let one client pin 120 sockets for hours. One feed page opens five streams (one
            // per tier), so 20 permits allows ~4 concurrent tabs. QueueLimit 0 rejects immediately
            // with 429 rather than parking the request: a queued EventSource just hangs.
            options.AddPolicy("sse", context =>
                RateLimitPartition.GetConcurrencyLimiter(
                    IpKey(context),
                    _ => new ConcurrencyLimiterOptions { PermitLimit = 20, QueueLimit = 0 }));
        });

    /// <summary>
    /// The bucket a request is counted in: the account when there is one, the address otherwise.
    ///
    /// The prefixes matter. Without them a user id could collide with an IP string and two
    /// unrelated callers would share a budget. The anonymous fallback is per-address rather than a
    /// single shared "anonymous" bucket — that older behaviour meant one unauthenticated caller
    /// could exhaust the budget for every other unauthenticated caller at once, which is a denial
    /// of service handed out for free.
    /// </summary>
    private static string CallerKey(HttpContext context) =>
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value is { Length: > 0 } userId
            ? $"u:{userId}"
            : IpKey(context);

    private static string IpKey(HttpContext context) =>
        $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
