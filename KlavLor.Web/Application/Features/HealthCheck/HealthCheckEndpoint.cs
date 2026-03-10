using KlavLor.Infrastructure.Persistence.EntityFramework;

namespace KlavLor.Web.Application.Features.HealthCheck;

public sealed class HealthCheckEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.HealthCheck, Endpoint).AllowAnonymous();
    }

    private static async Task<IResult> Endpoint(IDatabaseConnector databaseConnector)
    {
        var canConnect = await databaseConnector.CanConnect();
        return canConnect
            ? Microsoft.AspNetCore.Http.Results.Ok(new { Status = "Healthy" })
            : Microsoft.AspNetCore.Http.Results.StatusCode(503);
    }
}
