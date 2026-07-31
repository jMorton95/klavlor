using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Login;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Login;

public sealed class LoginModalEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.LoginModal.FromApi(), GetModal).AllowAnonymous();
        return app.MapPost(AppRoutes.LoginModal.FromApi(), PostModal).AllowAnonymous().RequireRateLimiting("login");
    }

    private static HtmxRetargetResult GetModal()
    {
        return IResultExtensions.HtmxRetargetResult<LoginModal>(
            "#hx-modal-container",
            swapOverride: "innerHTML");
    }

    private static async Task<IResult> PostModal(
        [FromForm] LoginCommand loginCommand,
        ISessionStateManager sessionManager,
        LoginHandler handler)
    {
        var result = await handler.Handle(loginCommand);

        if (result is { IsSuccess: true })
        {
            var userRoles = result.Value.UserRoles.Select(r => r.Role?.Name.ToString()).OfType<string>().ToArray();
            await sessionManager.LoginAsync(result.Value.Id, userRoles, result.Value.SecurityStamp);
            return IResultExtensions.HtmxRefresh();
        }

        if (result.ValidationErrors is not null)
        {
            return IResultExtensions.Component<LoginModal>(new { loginCommand, result.ValidationErrors });
        }

        var errorMessage = result.ErrorMessage ?? "Unable to login. Please contact your administrator.";
        return IResultExtensions.Component<LoginModal>(new { loginCommand, ErrorMessage = errorMessage });
    }
}
