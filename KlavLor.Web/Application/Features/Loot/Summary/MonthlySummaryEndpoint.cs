using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Loot.Summary;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Loot.Summary;

/// <summary>
/// The signed-in user's monthly summary, and the header button that leads to it.
/// </summary>
/// <remarks>
/// THE PAGE PAINTS BEFORE IT QUERIES. The page route serves a query-free shell and each of the four
/// panels fetches itself on load, in its own request scope — the character page's DeferredSection
/// pattern. Each panel resolves the month and character for itself; MonthlySummaryHandler memoises
/// that per user so the four requests share one read.
///
/// The shell resolves the month (pure, no query) and hands every panel the SAME explicit month, so a
/// page opened a moment before midnight on the last day cannot have panels disagree about which
/// month "default" meant.
///
/// User-only, every route: the summary is one person's own characters, never a public surface.
///
/// MUST be added to the explicit list in ConfigureEndpoints.cs — endpoint classes are not
/// auto-registered, and an unregistered one silently 404s.
/// </remarks>
public sealed class MonthlySummaryEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.LootSummaryWidget.FromApi(), GetWidget)
            .RequireAuthorization(nameof(RoleName.User))
            .RequireRateLimiting("read");

        // The panels. No HtmxNavigationFilter: they are fragments swapped into a shell that already
        // exists, never a navigation.
        app.MapGet(AppRoutes.LootSummaryOverview.FromApi(), GetOverview)
            .RequireAuthorization(nameof(RoleName.User))
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.LootSummaryCollection.FromApi(), GetCollection)
            .RequireAuthorization(nameof(RoleName.User))
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.LootSummarySources.FromApi(), GetSources)
            .RequireAuthorization(nameof(RoleName.User))
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.LootSummaryDrops.FromApi(), GetDrops)
            .RequireAuthorization(nameof(RoleName.User))
            .RequireRateLimiting("read");

        return app.MapGet(AppRoutes.LootSummary.FromApi(), Get)
            .RequireAuthorization(nameof(RoleName.User))
            .AddEndpointFilter<HtmxNavigationFilter>()
            .RequireRateLimiting("read");
    }

    private static RazorComponentResult Get([FromQuery] string? month, [FromQuery] int? characterId) =>
        IResultExtensions.Component<MonthlySummaryContent>(new { Month = month, CharacterId = characterId });

    // 204 for "nothing to show" leaves the layout's empty placeholder where it is, exactly as the
    // sidebar account block does — no flash, no error block.
    private static async Task<IResult> GetWidget([FromQuery] string? dismissed, MonthlySummaryHandler handler)
    {
        var widget = await handler.Widget(dismissed);
        return widget is null
            ? TypedResults.NoContent()
            : IResultExtensions.Component<SummaryWidgetCard>(new { Widget = widget });
    }

    private static async Task<IResult> GetOverview(
        [FromQuery] string? month, [FromQuery] int? characterId, MonthlySummaryHandler handler)
    {
        var result = await handler.Overview(month, characterId);
        return result.IsSuccess
            ? IResultExtensions.Component<SummaryOverviewPanel>(new { Overview = result.Value })
            : Results.NotFound();
    }

    private static async Task<IResult> GetCollection(
        [FromQuery] string? month, [FromQuery] int? characterId, MonthlySummaryHandler handler)
    {
        var result = await handler.Collection(month, characterId);
        if (!result.IsSuccess) return Results.NotFound();
        return result.Value is { } collection
            ? IResultExtensions.Component<SummaryCollectionPanel>(new { Collection = collection })
            : NothingPlayed();
    }

    private static async Task<IResult> GetSources(
        [FromQuery] string? month, [FromQuery] int? characterId, MonthlySummaryHandler handler)
    {
        var result = await handler.Sources(month, characterId);
        if (!result.IsSuccess) return Results.NotFound();
        return result.Value is { } sources
            ? IResultExtensions.Component<SummarySourcesPanel>(new { Sources = sources })
            : NothingPlayed();
    }

    private static async Task<IResult> GetDrops(
        [FromQuery] string? month, [FromQuery] int? characterId, MonthlySummaryHandler handler)
    {
        var result = await handler.Drops(month, characterId);
        if (!result.IsSuccess) return Results.NotFound();
        return result.Value is { } drops
            ? IResultExtensions.Component<SummaryDropsPanel>(new { Drops = drops })
            : NothingPlayed();
    }

    // A month with no character at all is said once, by the overview. The other panels answer with
    // an empty 200, which the outerHTML swap turns into removing their placeholder — three more
    // "nothing here" boxes would only repeat it. Not a 204: that would leave the skeletons standing.
    private static IResult NothingPlayed() => Results.Content(string.Empty, "text/html");
}
