using KlavLor.Application.Features.Templates.Delete;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;
using KlavLor.Web.Components.Generic.Modals;
using KlavLor.Web.Enums;

namespace KlavLor.Web.Application.Features.Templates.Commands;

public sealed class TemplateDeleteEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.TemplatesDelete.FromApi(), GetConfirmation).RequireAuthorization(nameof(RoleName.User));
        return app.MapDelete(AppRoutes.TemplatesDelete.FromApi(), DeleteEndpoint).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("mutation");
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

    private static async Task<IResult> DeleteEndpoint(
        int id,
        ISessionStateManager sessionManager,
        TemplateDeleteHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        await handler.Handle(new TemplateDeleteCommand(id));

        return IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch);
    }
}
