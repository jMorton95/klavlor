using KlavLor.Application.Features.Characters;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;
using Microsoft.AspNetCore.Mvc;

namespace KlavLor.Web.Application.Features.Characters;

public sealed class CharacterEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.Characters.FromApi(), GetCharacters)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapPost(AppRoutes.CharacterUpdateName.FromApi(), UpdateName)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapPost(AppRoutes.CharacterToggleVisibility.FromApi(), ToggleVisibility)
            .RequireAuthorization(nameof(RoleName.User));

        return app.MapPost(AppRoutes.AdminCharacterAssign.FromApi(), AssignUnassigned)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<RazorComponentResult> GetCharacters(CharacterHandler handler)
    {
        var result = await handler.HandleList();
        return IResultExtensions.Component<CharacterList>(new { Characters = result.Value });
    }

    private static async Task<RazorComponentResult> UpdateName(
        int id,
        [FromForm] string? displayName,
        CharacterHandler handler)
    {
        await handler.HandleUpdateName(id, displayName);
        var result = await handler.HandleList();
        return IResultExtensions.Component<CharacterList>(new { Characters = result.Value });
    }

    private static async Task<RazorComponentResult> ToggleVisibility(
        int id,
        CharacterHandler handler)
    {
        await handler.HandleToggleVisibility(id);
        var result = await handler.HandleList();
        return IResultExtensions.Component<CharacterList>(new { Characters = result.Value });
    }

    private static async Task<RazorComponentResult> AssignUnassigned(
        int id,
        CharacterHandler handler)
    {
        await handler.HandleAssignUnassigned(id);
        var result = await handler.HandleList();
        return IResultExtensions.Component<CharacterList>(new { Characters = result.Value });
    }
}
