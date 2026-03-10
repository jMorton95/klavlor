using KlavLor.Application.Features.Viewer.ViewerData;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Viewer;

public sealed class ViewerEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.TemplatesView.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<IResult> Endpoint(
        int id,
        ISessionStateManager sessionManager,
        ViewerDataHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        var result = await handler.Handle(new ViewerDataQuery { TemplateId = id }, userId);
        if (!result.IsSuccess) return IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch);

        return IResultExtensions.Component<ViewerPage>(new
        {
            Template = result.Value.Template,
            CompletedNodeIds = result.Value.CompletedNodeIds,
            IsOwner = result.Value.IsOwner,
            CanTrackCompletion = result.Value.CanTrackCompletion
        });
    }
}
