using KlavLor.Application.Features.Viewer.ViewerData;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Viewer;

public sealed class ShareEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.TemplatesShare.FromApi(), Endpoint)
            .AllowAnonymous();
    }

    private static async Task<IResult> Endpoint(
        string token,
        ISessionStateManager sessionManager,
        ViewerDataHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();

        var result = await handler.Handle(new ViewerDataQuery { ShareToken = token }, userId);
        if (!result.IsSuccess) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        return IResultExtensions.Component<ViewerPage>(new
        {
            Template = result.Value.Template,
            CompletionDates = result.Value.CompletionDates,
            IsOwner = result.Value.IsOwner,
            CanTrackCompletion = result.Value.CanTrackCompletion
        });
    }
}
