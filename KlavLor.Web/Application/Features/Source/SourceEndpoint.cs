using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Source;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.HttpResults;

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

        app.MapGet(AppRoutes.SourceClogs.FromApi(), GetRecentClogs)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.SourceItems.FromApi(), GetItems)
            .RequireAuthorization(nameof(RoleName.User));

        return app.MapGet(AppRoutes.SourceTrend.FromApi(), GetTrend)
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

    private static async Task<RazorComponentResult> GetRecentClogs([FromQuery] string name, GlobalSourceHandler handler)
    {
        var events = await handler.GetRecentClogs(name);
        return IResultExtensions.Component<SourceRecentClogsPanel>(new { SourceName = name, Events = events });
    }

    private static async Task<RazorComponentResult> GetItems([FromQuery] string name, [FromQuery] string? searchTerm, GlobalSourceHandler handler)
    {
        var items = await handler.GetItemFrequency(name, searchTerm);
        return IResultExtensions.Component<SourceItemsPanel>(new { SourceName = name, Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> GetTrend([FromQuery] string name, GlobalSourceHandler handler)
    {
        var points = await handler.GetMonthlyTrend(name);
        return IResultExtensions.Component<SourceTrendPanel>(new { Points = points });
    }
}
