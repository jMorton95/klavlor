using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Users.Create;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Users.Commands;

public sealed class UserCreateEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.UsersCreate.FromApi(), GetPage).RequireAuthorization(nameof(RoleName.Admin));
        return app.MapPost(AppRoutes.UsersCreate.FromApi(), Endpoint).RequireAuthorization(nameof(RoleName.Admin));
    }

    private static RazorComponentResult GetPage()
    {
        return IResultExtensions.Component<UserForm>(new { Command = new UserCreateCommand() });
    }

    private static async Task<Results<HtmxRedirectResult, RazorComponentResult>> Endpoint(
        [FromForm] UserCreateCommand command,
        UserCreateHandler handler)
    {
        var result = await handler.Handle(command);

        return result switch
        {
            { IsSuccess: true } => IResultExtensions.HtmxRedirect(AppRoutes.UsersSearch),
            { ValidationErrors: not null } => IResultExtensions.Component<UserForm>(new { Command = command, result.ValidationErrors }),
            _ => IResultExtensions.Component<UserForm>(new { Command = command, ErrorMessage = result.ErrorMessage })
        };
    }
}
