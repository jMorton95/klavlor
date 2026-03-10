using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Templates.Search;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Templates.Queries;

public sealed class TemplateSearchEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.TemplatesSearch.FromApi(), Endpoint)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<Results<RazorComponentResult, HtmxRedirectResult>> Endpoint(
        [AsParameters] TemplateSearchQuery query,
        ISessionStateManager sessionManager,
        TemplateSearchHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        if (userId is null) return IResultExtensions.HtmxRedirect(AppRoutes.Login);

        var result = await handler.Handle(query, userId.Value);

        return IResultExtensions.Component<TemplatesSearchGrid>(new { Result = result.Value });
    }
}
