using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Templates.Search;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Templates.Queries;

public sealed class TemplateSearchEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.TemplatesSearch.FromApi(), Endpoint)
            .AllowAnonymous()
            .RequireRateLimiting("read");
    }

    private static async Task<RazorComponentResult> Endpoint(
        [AsParameters] TemplateSearchQuery query,
        ISessionStateManager sessionManager,
        TemplateSearchHandler handler)
    {
        var isAuthenticated = sessionManager.IsAuthenticated();

        var result = await handler.Handle(query);

        return IResultExtensions.Component<TemplatesSearchGrid>(new { Result = result.Value, IsAuthenticated = isAuthenticated });
    }
}
