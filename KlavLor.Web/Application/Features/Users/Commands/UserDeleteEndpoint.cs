using KlavLor.Application.Features.Users.Delete;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.HttpResults;
using KlavLor.Web.Components.Generic.Modals;
using KlavLor.Web.Components.Generic.Toast;
using KlavLor.Web.Enums;

namespace KlavLor.Web.Application.Features.Users.Commands;

public sealed class UserDeleteEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.UsersDelete.FromApi(), GetConfirmation).RequireAuthorization(nameof(RoleName.Admin)).RequireRateLimiting("read");
        return app.MapDelete(AppRoutes.UsersDelete.FromApi(), DeleteEndpoint).RequireAuthorization(nameof(RoleName.Admin)).RequireRateLimiting("mutation");
    }

    private static HtmxRetargetResult GetConfirmation(int id)
    {
        return IResultExtensions.HtmxRetargetResult<ConfirmationModal>(
            "#hx-modal-container",
            new
            {
                Title = "Delete User",
                Description = "Are you sure you want to delete this user? This action cannot be undone.",
                ConfirmRoute = AppRoutes.UsersDelete.WithId(id).FromApi(),
                ConfirmText = "Delete",
                ModalConfirmationType = ModalConfirmationType.Danger,
                Method = HxMethod.DELETE,
                HxTarget = "#hx-page-container",
                HxSwap = "innerHTML transition:true"
            },
            "innerHTML");
    }

    private static async Task<RazorComponentResult> DeleteEndpoint(int id, UserDeleteHandler handler)
    {
        var result = await handler.Handle(new UserDeleteCommand(id));

        return result.IsSuccess
            ? IResultExtensions.Component<Toast>(new { Type = KlavLor.Domain.Shared.NotificationType.Success, Title = "User Deleted", Message = "The user has been successfully deleted." })
            : IResultExtensions.Component<Toast>(new { Type = KlavLor.Domain.Shared.NotificationType.Error, Title = "Error", Message = result.ErrorMessage });
    }
}
