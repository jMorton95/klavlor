using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Home;

public class HomePageEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.Home.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static Task<HtmxRedirectResult> Endpoint(ISessionStateManager sessionManager)
    {
        var userId = sessionManager.GetUserSessionId();

        if (userId is null)
            return Task.FromResult(IResultExtensions.HtmxRedirect(AppRoutes.Login));

        return Task.FromResult(IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch));
    }
}
