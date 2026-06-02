using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Source;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Source;

public sealed class SourceEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.Source.FromApi(), GetSource)
            .RequireAuthorization(nameof(RoleName.User))
            .AddEndpointFilter<HtmxNavigationFilter>();

        app.MapGet(AppRoutes.SourcePlayers.FromApi(), GetPlayers)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.SourceDrops.FromApi(), GetDrops)
            .RequireAuthorization(nameof(RoleName.User));

        return app.MapGet(AppRoutes.SourceCoverage.FromApi(), GetCoverage)
            .RequireAuthorization(nameof(RoleName.User));
    }

    private static async Task<RazorComponentResult> GetSource([FromQuery] string name, GlobalSourceHandler handler)
    {
        // Sequential awaits — scoped DbContext is not safe under concurrent use.
        var overview = await handler.GetOverview(name);
        var topDrops = overview is null ? [] : await handler.GetTopDrops(name);

        return IResultExtensions.Component<GlobalSourceDetail>(new
        {
            SourceName = name,
            Overview = overview,
            TopDrops = topDrops
        });
    }

    private static async Task<RazorComponentResult> GetPlayers([FromQuery] string name, GlobalSourceHandler handler)
    {
        var players = await handler.GetPlayers(name);
        return IResultExtensions.Component<SourcePlayersSection>(new { SourceName = name, Players = players });
    }

    private static async Task<RazorComponentResult> GetDrops([FromQuery] string name, [FromQuery] string? searchTerm, GlobalSourceHandler handler)
    {
        var drops = await handler.SearchDrops(name, searchTerm);
        return IResultExtensions.Component<SourceDropsResults>(new { SourceName = name, Drops = drops, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> GetCoverage([FromQuery] string name, GlobalSourceHandler handler)
    {
        var coverage = await handler.GetCollectionCoverage(name);
        return IResultExtensions.Component<SourceCoveragePanel>(new { Coverage = coverage });
    }
}
