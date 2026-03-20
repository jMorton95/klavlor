using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

/// <summary>
/// Shared rate limiter for all outbound OSRS Wiki HTTP requests.
/// Keeps cumulative throughput under ~250 req/min as a courtesy to the wiki.
/// The rate limiter is static so all transient handler instances share the same budget.
/// </summary>
public sealed class OsrsWikiRateLimitHandler(ILogger<OsrsWikiRateLimitHandler> logger) : DelegatingHandler
{
    private static readonly TokenBucketRateLimiter RateLimiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 15,
        TokensPerPeriod = 4,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 50,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true
    });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await RateLimiter.AcquireAsync(1, cancellationToken);

        if (!lease.IsAcquired)
        {
            logger.LogWarning("Wiki rate limiter queue full — rejecting request");
            return new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
