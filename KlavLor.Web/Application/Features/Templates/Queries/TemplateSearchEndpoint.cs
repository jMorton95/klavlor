using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Templates.Search;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Templates.Queries;

public sealed class TemplateSearchEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.TemplatesSearch.FromApi(), Endpoint)
            .AllowAnonymous();
    }

    private static async Task<RazorComponentResult> Endpoint(
        [AsParameters] TemplateSearchQuery query,
        ISessionStateManager sessionManager,
        TemplateSearchHandler handler)
    {
        var userId = sessionManager.GetUserSessionId();
        var isAuthenticated = userId.HasValue;
        var isAdmin = sessionManager.IsUserSessionAdministrator();

        var result = await handler.Handle(query, userId, isAdmin);

        return IResultExtensions.Component<TemplatesSearchGrid>(new { Result = result.Value, IsAuthenticated = isAuthenticated });
    }
}
