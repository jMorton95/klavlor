using KlavLor.Application.Features.Characters;
using KlavLor.Application.Interfaces.Services;
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

        app.MapPost(AppRoutes.CharacterToggleLeagues.FromApi(), ToggleLeagues)
            .RequireAuthorization(nameof(RoleName.User));

        return app.MapPost(AppRoutes.AdminCharacterAssign.FromApi(), AssignUnassigned)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<RazorComponentResult> GetCharacters(CharacterHandler handler)
    {
        var result = await handler.HandleList();
        var unassigned = await handler.HandleUnassignedCount();
        return IResultExtensions.Component<CharacterList>(new { Characters = result.Value, UnassignedCount = unassigned });
    }

    private static async Task<RazorComponentResult> UpdateName(
        int id,
        [FromForm] string? displayName,
        CharacterHandler handler)
    {
        var updateResult = await handler.HandleUpdateName(id, displayName);
        var result = await handler.HandleList();
        var unassigned = await handler.HandleUnassignedCount();
        return IResultExtensions.Component<CharacterList>(new
        {
            Characters = result.Value,
            UnassignedCount = unassigned,
            ErrorMessage = updateResult.IsSuccess ? (string?)null : updateResult.ErrorMessage
        });
    }

    private static async Task<RazorComponentResult> ToggleVisibility(
        int id,
        CharacterHandler handler)
    {
        await handler.HandleToggleVisibility(id);
        var result = await handler.HandleList();
        var unassigned = await handler.HandleUnassignedCount();
        return IResultExtensions.Component<CharacterList>(new { Characters = result.Value, UnassignedCount = unassigned });
    }

    private static async Task<IResult> ToggleLeagues(
        int id,
        CharacterHandler handler,
        ISystemSettingsCache settings)
    {
        if (!settings.IsLeaguesEnabled) return TypedResults.NotFound();

        await handler.HandleToggleLeagues(id);
        var result = await handler.HandleList();
        var unassigned = await handler.HandleUnassignedCount();
        return IResultExtensions.Component<CharacterList>(new { Characters = result.Value, UnassignedCount = unassigned });
    }

    private static async Task<RazorComponentResult> AssignUnassigned(
        int id,
        CharacterHandler handler)
    {
        await handler.HandleAssignUnassigned(id);
        var result = await handler.HandleList();
        var unassigned = await handler.HandleUnassignedCount();
        return IResultExtensions.Component<CharacterList>(new { Characters = result.Value, UnassignedCount = unassigned });
    }
}
