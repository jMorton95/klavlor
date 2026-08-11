using KlavLor.Domain.Shared;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Templates.Builder;

public sealed class OsrsSearchEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        return app.MapGet(AppRoutes.OsrsSearch.FromApi(), Search).RequireAuthorization(nameof(RoleName.User)).RequireRateLimiting("upstream");
    }

    private static async Task<IResult> Search(string? q, IOsrsWikiClient wikiClient)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return IResultExtensions.Component<OsrsSearchResults>(new { Results = new List<OsrsSearchResult>() });

        var results = await wikiClient.SearchItems(q, 8);

        return IResultExtensions.Component<OsrsSearchResults>(new { Results = results });
    }
}
