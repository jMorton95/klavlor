using KlavLor.Application.Features.Templates.Duplicate;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Templates.ImportExport;

public sealed class DuplicateEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapPost(AppRoutes.TemplatesDuplicate.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.User))
            .RequireRateLimiting("mutation");
    }

    private static async Task<HtmxRedirectResult> Endpoint(
        int id,
        ISessionStateManager sessionManager,
        DuplicateTemplateHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        var result = await handler.Handle(new DuplicateTemplateCommand { SourceTemplateId = id }, userId.Value);

        return result.IsSuccess
            ? IResultExtensions.HtmxRedirect(AppRoutes.Builder.WithId(result.Value.Id))
            : IResultExtensions.HtmxRedirect(AppRoutes.TemplatesSearch);
    }
}
