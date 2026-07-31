using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Login;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Login;

public sealed class LoginEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapPost(AppRoutes.Login.FromApi(), Endpoint).AllowAnonymous().RequireRateLimiting("login");
    }

    private static async Task<Results<HtmxRedirectResult, RazorComponentResult>> Endpoint(
        [FromForm] LoginCommand loginCommand,
        ISessionStateManager sessionManager,
        LoginHandler handler)
    {
        var result = await handler.Handle(loginCommand);

        if (result is { IsSuccess: false })
            return IResultExtensions.Component<LoginPage>(new { loginCommand, result.ErrorMessage });

        var userRoles = result.Value.UserRoles.Select(r => r.Role?.Name.ToString()).OfType<string>().ToArray();

        await sessionManager.LoginAsync(result.Value.Id, userRoles, result.Value.SecurityStamp);

        return result switch
        {
            { IsSuccess: true } => IResultExtensions.HtmxRedirect(AppRoutes.Home),
            { ValidationErrors: not null } => IResultExtensions.Component<LoginPage>(new { loginCommand, result.ValidationErrors }),
            _ => IResultExtensions.Component<LoginPage>(new { loginCommand, ErrorMessage = "Unable to login. Please contact your administrator." })
        };
    }
}
