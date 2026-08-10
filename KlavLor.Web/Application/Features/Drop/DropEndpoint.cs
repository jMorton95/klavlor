using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Drop;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Drop;

public sealed class DropEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.Drop.FromApi(), GetDrop)
            .RequireAuthorization(nameof(RoleName.User))
            .AddEndpointFilter<HtmxNavigationFilter>();

        app.MapGet(AppRoutes.DropSources.FromApi(), GetSources)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.DropCharacters.FromApi(), GetCharacters)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.DropTrend.FromApi(), GetTrend)
            .RequireAuthorization(nameof(RoleName.User));

        app.MapGet(AppRoutes.DropSessions.FromApi(), GetSessions)
            .RequireAuthorization(nameof(RoleName.User));

        // One character's sources for this item. A full page rather than a panel, so it pushes a
        // URL like the drop page itself does.
        return app.MapGet(AppRoutes.DropCharacterSources.FromApi(), GetCharacterSources)
            .RequireAuthorization(nameof(RoleName.User))
            .AddEndpointFilter<HtmxNavigationFilter>();
    }

    private static async Task<RazorComponentResult> GetDrop([FromQuery] string name, GlobalDropHandler handler)
    {
        var overview = await handler.GetOverview(name);
        return IResultExtensions.Component<GlobalDropDetail>(new
        {
            ItemName = name,
            Overview = overview
        });
    }

    private static async Task<RazorComponentResult> GetSources(
        [FromQuery] string name,
        [FromQuery] string? sortBy,
        [FromQuery] SortDirection? sortDirection,
        [FromQuery] string? searchTerm,
        GlobalDropHandler handler)
    {
        var table = await handler.GetSources(name, sortBy, sortDirection, searchTerm);

        // A sort/filter request re-renders just the grid; the initial (default) load returns
        // the full section (search box + grid).
        if (sortBy is not null || !string.IsNullOrWhiteSpace(searchTerm))
            return IResultExtensions.Component<DropSourcesGrid>(new { ItemName = name, Table = table });

        return IResultExtensions.Component<DropSourcesSection>(new { ItemName = name, Table = table });
    }

    private static async Task<RazorComponentResult> GetCharacters(
        [FromQuery] string name,
        [FromQuery] string? sortBy,
        [FromQuery] SortDirection? sortDirection,
        [FromQuery] string? searchTerm,
        GlobalDropHandler handler)
    {
        var table = await handler.GetCharacters(name, sortBy, sortDirection, searchTerm);

        if (sortBy is not null || !string.IsNullOrWhiteSpace(searchTerm))
            return IResultExtensions.Component<DropCharactersGrid>(new { ItemName = name, Table = table });

        return IResultExtensions.Component<DropCharactersSection>(new { ItemName = name, Table = table });
    }

    // characterId scopes the panel to one character for the per-character page; absent is the
    // all-players view. Same endpoint and same component either way, so the two pages can't drift.
    private static async Task<RazorComponentResult> GetTrend(
        [FromQuery] string name,
        [FromQuery] int? characterId,
        GlobalDropHandler handler)
    {
        var points = await handler.GetMonthlyTrend(name, characterId);
        return IResultExtensions.Component<DropTrendPanel>(new { Points = points });
    }

    private static async Task<RazorComponentResult> GetCharacterSources(
        [FromQuery] string name,
        [FromQuery] int characterId,
        GlobalDropHandler handler)
    {
        var data = await handler.GetCharacterSources(name, characterId);
        return IResultExtensions.Component<DropCharacterSourcesDetail>(new
        {
            ItemName = name,
            Data = data
        });
    }

    private static async Task<RazorComponentResult> GetSessions(
        [FromQuery] string name,
        [FromQuery] int? characterId,
        GlobalDropHandler handler)
    {
        var sessions = await handler.GetRecentSessions(name, characterId);
        return IResultExtensions.Component<DropSessionsPanel>(new { Sessions = sessions });
    }
}
