using KlavLor.Application.Features.Viewer.ViewerData;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Viewer;

public sealed class ViewerEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.TemplatesView.FromApi(), Endpoint)
            .AllowAnonymous();
    }

    private static async Task<IResult> Endpoint(
        int id,
        ISessionStateManager sessionManager,
        ViewerDataHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();

        var result = await handler.Handle(new ViewerDataQuery { TemplateId = id }, userId);
        if (!result.IsSuccess) return IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch);

        var template = result.Value.Template;

        // Strip ShareToken for non-owners (defense-in-depth)
        if (!result.Value.IsOwner)
        {
            template.ShareToken = null!;
        }

        return IResultExtensions.Component<ViewerPage>(new
        {
            Template = template,
            CompletedNodeIds = result.Value.CompletedNodeIds,
            IsOwner = result.Value.IsOwner,
            CanTrackCompletion = result.Value.CanTrackCompletion
        });
    }
}
