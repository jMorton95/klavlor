using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Home;

public class HomePageEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.Home.FromApi(), Endpoint)
            .AllowAnonymous();
    }

    private static Task<HtmxRedirectResult> Endpoint()
    {
        return Task.FromResult(IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch));
    }
}
