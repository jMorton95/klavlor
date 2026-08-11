using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Home;

public sealed class HomePageEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.Home.FromApi(), Endpoint)
            .AllowAnonymous()
            .RequireRateLimiting("read");
    }

    private static Task<HtmxRedirectResult> Endpoint()
    {
        return Task.FromResult(IResultExtensions.HtmxRedirect(AppRoutes.LootFeed));
    }
}
