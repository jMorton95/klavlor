using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Users.Edit;
using KlavLor.Application.Features.Users.GetById;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Users.Commands;

public sealed class UserEditEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.UsersEdit.FromApi(), GetPage).RequireAuthorization(nameof(RoleName.Admin));
        return app.MapPost(AppRoutes.UsersEdit.FromApi(), Endpoint).RequireAuthorization(nameof(RoleName.Admin));
    }

    private static async Task<Results<RazorComponentResult, HtmxRedirectResult>> GetPage(
        int id, UserGetByIdHandler handler)
    {
        var result = await handler.Handle(new UserGetByIdQuery(id));

        if (!result.IsSuccess)
            return IResultExtensions.HtmxRedirect(AppRoutes.UsersSearch);

        var command = new UserEditCommand
        {
            Id = result.Value.Id,
            FirstName = result.Value.FirstName,
            LastName = result.Value.LastName,
            Email = result.Value.Email,
            IsActive = result.Value.IsActive
        };

        return IResultExtensions.Component<UserForm>(new { Command = command, IsEditing = true });
    }

    private static async Task<Results<HtmxRedirectResult, RazorComponentResult>> Endpoint(
        int id,
        [FromForm] UserEditCommand command,
        UserEditHandler handler)
    {
        command.Id = id;
        var result = await handler.Handle(command);

        return result switch
        {
            { IsSuccess: true } => IResultExtensions.HtmxRedirect(AppRoutes.UsersSearch),
            { ValidationErrors: not null } => IResultExtensions.Component<UserForm>(new { Command = command, result.ValidationErrors, IsEditing = true }),
            _ => IResultExtensions.Component<UserForm>(new { Command = command, ErrorMessage = result.ErrorMessage, IsEditing = true })
        };
    }
}
