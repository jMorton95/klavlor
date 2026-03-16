using KlavLor.Application.Features.Viewer.ViewerData;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Viewer;

public sealed class ViewerEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.TemplatesView.FromApi(), Endpoint)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous");
    }

    private static async Task<IResult> Endpoint(
        int id,
        ISessionStateManager sessionManager,
        ViewerDataHandler handler)
    {
        var result = await handler.Handle(new ViewerDataQuery { TemplateId = id });
        if (!result.IsSuccess) return IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch);

        return IResultExtensions.Component<ViewerPage>(new
        {
            Template = result.Value.Template,
            CompletionDates = result.Value.CompletionDates,
            IsOwner = result.Value.IsOwner,
            CanTrackCompletion = result.Value.CanTrackCompletion
        });
    }
}
