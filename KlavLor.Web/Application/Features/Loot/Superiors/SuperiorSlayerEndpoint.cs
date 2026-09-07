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
/// THREE ROUTES, because the page paints before it queries. The page route serves a query-free
/// shell; the table and the receipts each fetch themselves on load, in their own request scope. It
/// was one route rendering everything during SSR, which meant nothing at all appeared until every
/// query had run and 239KB of markup had been built - measured at 1.25s on a cold process.
///
/// Both halves read the SAME cached aggregate, so the pair costs one set of queries rather than
/// two: whichever arrives first fills the 5-minute entry and the other hits it. The receipts half
/// deliberately asks for the DEFAULT sort - sorting is applied after the cache and only reorders
/// the table, so passing the sort through would be a second cache key for identical receipts.
///
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
        // The two halves. No HtmxNavigationFilter on either: they are fragments swapped into a
        // shell that already exists, never a whole page, so there is nothing to wrap them in.
        app.MapGet(AppRoutes.LootSuperiorsTable.FromApi(), GetTable)
            .AllowAnonymous()
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.LootSuperiorsReceipts.FromApi(), GetReceipts)
            .AllowAnonymous()
            .RequireRateLimiting("read");

        // The shell, for in-app HTMX navigation. Query-free.
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
    private static RazorComponentResult Get([FromQuery] int? characterId, [FromQuery] bool? asc) =>
        IResultExtensions.Component<SuperiorsContent>(new { CharacterId = characterId, Asc = asc });

    // The comparison table. Also the target of every sort link, which is why the sort parameters
    // live here as well as on the shell: a sort re-fetches this half alone and leaves the receipts,
    // which no ordering affects, exactly where they are.
    private static async Task<RazorComponentResult> GetTable(
        [FromQuery] int? characterId,
        [FromQuery] bool? asc,
        SuperiorSlayerHandler handler)
    {
        var comparison = await handler.Get(new SuperiorSort(characterId, asc ?? false));
        return IResultExtensions.Component<SuperiorMatrix>(new { Comparison = comparison });
    }

    private static async Task<RazorComponentResult> GetReceipts(SuperiorSlayerHandler handler)
    {
        var comparison = await handler.Get();
        return IResultExtensions.Component<SuperiorUniquesPanel>(new { Drops = comparison.RecentUniques });
    }
}
