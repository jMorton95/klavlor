using KlavLor.Application.Features.Templates.Delete;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;
using KlavLor.Web.Components.Generic.Modals;
using KlavLor.Web.Components.Generic.Toast;
using KlavLor.Web.Enums;

namespace KlavLor.Web.Application.Features.Templates.Commands;

public sealed class TemplateDeleteEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.TemplatesDelete.FromApi(), GetConfirmation).RequireAuthorization(nameof(RoleName.User));
        return app.MapDelete(AppRoutes.TemplatesDelete.FromApi(), DeleteEndpoint).RequireAuthorization(nameof(RoleName.User));
    }

    private static HtmxRetargetResult GetConfirmation(int id)
    {
        return IResultExtensions.HtmxRetargetResult<ConfirmationModal>(
            "#hx-modal-container",
            new
            {
                Title = "Delete Template",
                Description = "Are you sure you want to delete this template? All nodes and edges will be permanently removed.",
                ConfirmRoute = AppRoutes.TemplatesDelete.WithId(id).FromApi(),
                ConfirmText = "Delete",
                ModalConfirmationType = ModalConfirmationType.Danger,
                Method = HxMethod.DELETE,
                HxTarget = "#hx-page-container",
                HxSwap = "innerHTML transition:true"
            },
            "innerHTML");
    }

    private static async Task<RazorComponentResult> DeleteEndpoint(
        int id,
        ISessionStateManager sessionManager,
        TemplateDeleteHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null)
            return IResultExtensions.Component<Toast>(new { Type = KlavLor.Domain.Shared.NotificationType.Error, Title = "Error", Message = "Not authenticated." });

        var result = await handler.Handle(new TemplateDeleteCommand(id), userId.Value);

        return result.IsSuccess
            ? IResultExtensions.Component<Toast>(new { Type = KlavLor.Domain.Shared.NotificationType.Success, Title = "Template Deleted", Message = "The template has been successfully deleted." })
            : IResultExtensions.Component<Toast>(new { Type = KlavLor.Domain.Shared.NotificationType.Error, Title = "Error", Message = result.ErrorMessage });
    }
}
