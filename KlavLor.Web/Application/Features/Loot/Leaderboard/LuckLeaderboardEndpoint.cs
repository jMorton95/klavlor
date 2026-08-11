using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Loot.Leaderboard;
using KlavLor.Domain.Entities;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Loot.Leaderboard;

public sealed class LuckLeaderboardEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        // Public, like the rest of the loot read surface (feed, character logs): the sidebar
        // shows the Luck Leaderboard to signed-out visitors, and the board contains no
        // per-user data — LuckLeaderboardHandler reads a precomputed global board.
        app.MapGet(AppRoutes.LootLeaderboard.FromApi(), GetPage)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>();

        return app.MapGet(AppRoutes.LootLeaderboardResults.FromApi(), GetResults)
            .AllowAnonymous()
            .RequireRateLimiting("read");
    }

    // Serves the content for in-app HTMX navigation; the HtmxNavigationFilter wraps it in the
    // full shell for a non-HTMX request. Direct browser loads hit the routable LuckLeaderboardPage.
    private static RazorComponentResult GetPage()
        => IResultExtensions.Component<LuckLeaderboardContent>();

    private static async Task<RazorComponentResult> GetResults(
        [FromQuery] LeaderboardBoard? board,
        LuckLeaderboardHandler handler)
    {
        var selected = board ?? LeaderboardBoard.Spoon;
        var rows = await handler.Get(selected);
        return IResultExtensions.Component<LuckLeaderboardResults>(new { Rows = rows, Board = selected });
    }
}
