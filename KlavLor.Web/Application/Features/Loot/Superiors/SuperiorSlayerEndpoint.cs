using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Loot.Superiors;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Loot.Superiors;

/// <summary>
/// The Superior Slayer comparison: every superior slayer monster by Slayer level, with each tracked
/// character's kill count of each, and the shared unique table's receipts.
/// </summary>
/// <remarks>
/// PUBLIC, and deliberately so. The house rule stated on CollectionLogEndpoint is that
/// cross-character comparison surfaces are clan-internal and authenticated; this one follows the
/// Luck Leaderboard instead, by explicit decision. What it exposes is a kill count per monster - no
/// values, no collection log, no per-user data - and it sits in the public sidebar, so a nav link
/// behind an authorization policy would 401-redirect a signed-out visitor to login.
///
/// MUST be added to the explicit list in ConfigureEndpoints.cs - endpoint classes are not
/// auto-registered, and an unregistered one silently 404s.
/// </remarks>
public sealed class SuperiorSlayerEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        // One route. The handler serves a single cached aggregate for the whole page, so there is no
        // second panel to stagger and nothing to fetch on a later trigger.
        return app.MapGet(AppRoutes.LootSuperiors.FromApi(), Get)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>()
            .RequireRateLimiting("read");
    }

    // Serves the content for in-app HTMX navigation; HtmxNavigationFilter wraps it in the full shell
    // for a non-HTMX request. Direct browser loads hit the routable SuperiorSlayerPage instead.
    //
    // The sort lives in the query string rather than in component state so a sorted view is
    // linkable and survives a refresh - the header links push it with hx-push-url, and the routable
    // page reads the same two parameters.
    private static async Task<RazorComponentResult> Get(
        [FromQuery] int? characterId,
        [FromQuery] bool? asc,
        SuperiorSlayerHandler handler)
    {
        var sort = new SuperiorSort(characterId, asc ?? false);
        var comparison = await handler.Get(sort);
        return IResultExtensions.Component<SuperiorsContent>(new { Comparison = comparison, Sort = sort });
    }
}
