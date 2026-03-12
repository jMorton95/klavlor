using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Logout;

public class LogoutEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapPost(AppRoutes.Logout.FromApi(), Endpoint).RequireAuthorization();
    }

    public static async Task<HtmxRedirectResult> Endpoint([FromServices] ISessionStateManager sessionManager)
    {
        await sessionManager.LogoutAsync();
        return IResultExtensions.HtmxRedirect(AppRoutes.Login);
    }
}
