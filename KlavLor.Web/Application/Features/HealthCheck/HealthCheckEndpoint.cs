using KlavLor.Infrastructure.Persistence.EntityFramework;

namespace KlavLor.Web.Application.Features.HealthCheck;

public sealed class HealthCheckEndpoint : IEndpoint
{
    /// <remarks>
    /// The one route in the app with NO rate-limiting policy, deliberately. Swarm's health probe
    /// polls this from a fixed address on a fixed interval; a 429 is not a 200, so the container
    /// would be marked unhealthy and restarted. A limiter here would manufacture the outage it
    /// exists to detect. The handler is a single connectivity check and holds nothing worth
    /// scraping, so there is nothing to protect.
    /// </remarks>
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.HealthCheck, Endpoint).AllowAnonymous();
    }

    private static async Task<IResult> Endpoint(IDatabaseConnector databaseConnector)
    {
        var canConnect = await databaseConnector.CanConnect();
        return canConnect
            ? Results.Ok(new { Status = "Healthy" })
            : Results.StatusCode(503);
    }
}
