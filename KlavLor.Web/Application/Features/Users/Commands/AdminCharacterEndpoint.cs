using KlavLor.Application.Features.Characters;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;
using KlavLor.Web.Components.Generic.Modals;
using KlavLor.Web.Components.Generic.Toast;
using KlavLor.Web.Enums;

namespace KlavLor.Web.Application.Features.Users.Commands;

public sealed class AdminCharacterEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.AdminCharacterSection.FromApi(), GetCharacterSection)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapGet(AppRoutes.AdminUserDeleteLoot.FromApi(), GetDeleteLootConfirmation)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapDelete(AppRoutes.AdminUserDeleteLoot.FromApi(), DeleteAllUserLoot)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminCharacterToggleHidden.FromApi(), ToggleAdminHidden)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminCharacterToggleLeagues.FromApi(), ToggleAdminLeagues)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminCharacterToggleVisibility.FromApi(), ToggleAdminVisibility)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminCharacterUpdateName.FromApi(), UpdateAdminCharacterName)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapGet(AppRoutes.AdminCharacterDelete.FromApi(), GetDeleteCharacterConfirmation)
            .RequireAuthorization(nameof(RoleName.Admin));

        return app.MapDelete(AppRoutes.AdminCharacterDelete.FromApi(), DeleteCharacterData)
            .RequireAuthorization(nameof(RoleName.Admin));
    }

    private static async Task<RazorComponentResult> GetCharacterSection(int id, CharacterHandler handler)
    {
        var result = await handler.HandleListForUser(id);
        return IResultExtensions.Component<AdminCharacterSection>(new { UserId = id, Characters = result.Value });
    }

    private static HtmxRetargetResult GetDeleteLootConfirmation(int id)
    {
        return IResultExtensions.HtmxRetargetResult<ConfirmationModal>(
            "#hx-modal-container",
            new
            {
                Title = "Delete All Loot Data",
                Description = "This will permanently delete all loot records and characters for this user. This action cannot be undone.",
                ConfirmText = "Delete All Loot",
                ConfirmRoute = AppRoutes.AdminUserDeleteLoot.WithId(id).FromApi(),
                ModalConfirmationType = ModalConfirmationType.Danger,
                Method = HxMethod.DELETE,
                HxTarget = "#hx-page-container",
                HxSwap = "innerHTML transition:true"
            },
            "innerHTML");
    }

    private static async Task<RazorComponentResult> DeleteAllUserLoot(int id, CharacterHandler handler)
    {
        var result = await handler.HandleDeleteAllUserData(id);
        return result.IsSuccess
            ? IResultExtensions.Component<Toast>(new { Type = NotificationType.Success, Title = "Loot Data Deleted", Message = "All loot data for this user has been deleted." })
            : IResultExtensions.Component<Toast>(new { Type = NotificationType.Error, Title = "Error", Message = result.ErrorMessage });
    }

    private static async Task<RazorComponentResult> ToggleAdminHidden(int id, int characterId, CharacterHandler handler)
    {
        await handler.HandleToggleAdminHidden(characterId);
        var result = await handler.HandleListForUser(id);
        return IResultExtensions.Component<AdminCharacterSection>(new { UserId = id, Characters = result.Value });
    }

    private static async Task<IResult> ToggleAdminLeagues(int id, int characterId, CharacterHandler handler, ISystemSettingsCache settings)
    {
        if (!settings.IsLeaguesEnabled) return TypedResults.NotFound();

        await handler.HandleToggleLeagues(characterId);
        var result = await handler.HandleListForUser(id);
        return IResultExtensions.Component<AdminCharacterSection>(new { UserId = id, Characters = result.Value });
    }

    private static async Task<RazorComponentResult> ToggleAdminVisibility(int id, int characterId, CharacterHandler handler)
    {
        await handler.HandleToggleVisibility(characterId);
        var result = await handler.HandleListForUser(id);
        return IResultExtensions.Component<AdminCharacterSection>(new { UserId = id, Characters = result.Value });
    }

    private static async Task<RazorComponentResult> UpdateAdminCharacterName(
        int id,
        int characterId,
        [Microsoft.AspNetCore.Mvc.FromForm] string? displayName,
        CharacterHandler handler)
    {
        var updateResult = await handler.HandleUpdateName(characterId, displayName);
        var result = await handler.HandleListForUser(id);
        return IResultExtensions.Component<AdminCharacterSection>(new
        {
            UserId = id,
            Characters = result.Value,
            ErrorMessage = updateResult.IsSuccess ? (string?)null : updateResult.ErrorMessage
        });
    }

    private static HtmxRetargetResult GetDeleteCharacterConfirmation(int id, int characterId)
    {
        return IResultExtensions.HtmxRetargetResult<ConfirmationModal>(
            "#hx-modal-container",
            new
            {
                Title = "Delete Character Data",
                Description = "This will permanently delete this character and all its loot records. This action cannot be undone.",
                ConfirmText = "Delete Character",
                ConfirmRoute = AppRoutes.AdminCharacterDelete.Replace("{id:int}", id.ToString()).Replace("{characterId:int}", characterId.ToString()).FromApi(),
                ModalConfirmationType = ModalConfirmationType.Danger,
                Method = HxMethod.DELETE,
                HxTarget = "#hx-page-container",
                HxSwap = "innerHTML transition:true"
            },
            "innerHTML");
    }

    private static async Task<RazorComponentResult> DeleteCharacterData(int id, int characterId, CharacterHandler handler)
    {
        var result = await handler.HandleDeleteCharacterData(characterId);
        return result.IsSuccess
            ? IResultExtensions.Component<Toast>(new { Type = NotificationType.Success, Title = "Character Deleted", Message = "Character and all its loot records have been deleted." })
            : IResultExtensions.Component<Toast>(new { Type = NotificationType.Error, Title = "Error", Message = result.ErrorMessage });
    }
}
