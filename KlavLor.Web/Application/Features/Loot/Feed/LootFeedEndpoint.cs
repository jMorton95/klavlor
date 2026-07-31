using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Web.Application.HttpResults;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KlavLor.Web.Application.Features.Loot.Feed;

public sealed class LootFeedEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        // Main feed routes
        app.MapGet(AppRoutes.LootFeed.FromApi(), (LootFeedTiersHandler handler) => GetPage(handler, LootFeedScope.Main))
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootFeedGrid.FromApi(), (LootFeedTiersHandler handler, string? tiers) => GetGrid(handler, LootFeedScope.Main, tiers))
            .AllowAnonymous();

        MapStream(app, AppRoutes.LootFeedStreamStandard, LootFeedScope.Main, LootFeedTier.Standard);
        MapStream(app, AppRoutes.LootFeedStreamUncommon, LootFeedScope.Main, LootFeedTier.Uncommon);
        MapStream(app, AppRoutes.LootFeedStreamRare, LootFeedScope.Main, LootFeedTier.Rare);
        MapStream(app, AppRoutes.LootFeedStreamEpic, LootFeedScope.Main, LootFeedTier.Epic);
        MapStream(app, AppRoutes.LootFeedStreamLegendary, LootFeedScope.Main, LootFeedTier.Legendary);

        // Leagues feed routes — parallel set, filters to IsLeagues=true characters.
        // All Leagues endpoints short-circuit to 404 when the admin has disabled the feature.
        app.MapGet(AppRoutes.LootFeedLeagues.FromApi(), async (LootFeedTiersHandler handler, ISystemSettingsCache settings) =>
            {
                if (!settings.IsLeaguesEnabled) return TypedResults.NotFound();
                return await GetPage(handler, LootFeedScope.Leagues);
            })
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootFeedLeaguesGrid.FromApi(), async (LootFeedTiersHandler handler, ISystemSettingsCache settings, string? tiers) =>
            {
                if (!settings.IsLeaguesEnabled) return TypedResults.NotFound();
                return await GetGrid(handler, LootFeedScope.Leagues, tiers);
            })
            .AllowAnonymous();

        MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamStandard, LootFeedTier.Standard);
        MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamUncommon, LootFeedTier.Uncommon);
        MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamRare, LootFeedTier.Rare);
        MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamEpic, LootFeedTier.Epic);
        return MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamLegendary, LootFeedTier.Legendary);
    }

    private static RouteHandlerBuilder MapStream(IEndpointRouteBuilder app, string route, LootFeedScope scope, LootFeedTier tier) =>
        app.MapGet(route.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, scope, tier))
            .AllowAnonymous();

    private static RouteHandlerBuilder MapLeaguesStream(IEndpointRouteBuilder app, string route, LootFeedTier tier) =>
        app.MapGet(route.FromApi(), async (HttpContext ctx, ILootFeedService svc, ISystemSettingsCache settings, IServiceProvider sp, ILoggerFactory lf) =>
            {
                if (!settings.IsLeaguesEnabled)
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                await StreamFeed(ctx, svc, sp, lf, LootFeedScope.Leagues, tier);
            })
            .AllowAnonymous();

    private static async Task<IResult> GetPage(LootFeedTiersHandler handler, LootFeedScope scope)
    {
        var tierData = await handler.Handle(scope);

        return IResultExtensions.Component<LootFeedContent>(new
        {
            StandardEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Standard],
            UncommonEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Uncommon],
            RareEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Rare],
            EpicEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Epic],
            LegendaryEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Legendary],
            Scope = scope
        });
    }

    private static async Task<IResult> GetGrid(LootFeedTiersHandler handler, LootFeedScope scope, string? tiers)
    {
        var requestedTiers = ParseTiers(tiers);
        var tierData = await handler.Handle(scope, requestedTiers);

        return IResultExtensions.Component<LootFeedGrid>(new
        {
            StandardEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Standard],
            UncommonEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Uncommon],
            RareEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Rare],
            EpicEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Epic],
            LegendaryEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Legendary],
            ActiveTiers = requestedTiers is not null
                ? (IReadOnlyList<LootFeedTier>)requestedTiers.Order().ToList()
                : (IReadOnlyList<LootFeedTier>)ILootFeedService.AllTiers,
            Scope = scope
        });
    }

    private static HashSet<LootFeedTier>? ParseTiers(string? tiers)
    {
        if (string.IsNullOrWhiteSpace(tiers)) return null;

        var result = new HashSet<LootFeedTier>();
        foreach (var name in tiers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<LootFeedTier>(name, ignoreCase: true, out var tier))
                result.Add(tier);
        }
        return result.Count > 0 ? result : null;
    }

    private static async Task StreamFeed(
        HttpContext context,
        ILootFeedService feedService,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        LootFeedScope scope,
        LootFeedTier tier)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        var ct = context.RequestAborted;

        await foreach (var broadcast in feedService.SubscribeAsync(scope, tier, ct))
        {
            var entry = broadcast.Entry;
            var prev = broadcast.PreviousDomId;

            // PreviousDomId semantics (see ILootFeedService / LootFeedService):
            //   null                        → brand-new entry: plain card, column prepends via afterbegin
            //   prev == entry.DomId         → in-place update (rare out-of-order merge): card carries hx-swap-oob="outerHTML"
            //   prev != entry.DomId         → bubble-up merge: emit OOB delete for the old card, then plain card to prepend
            var isInPlace = prev is not null && prev == entry.DomId;

            var cardHtml = await RenderComponentToString<LootFeedItem>(
                serviceProvider, loggerFactory,
                new Dictionary<string, object?>
                {
                    ["Entry"] = entry,
                    ["Animate"] = true,
                    ["IsMerge"] = isInPlace
                });

            string html;
            if (prev is not null && !isInPlace)
            {
                var deleteFragment = $"<div id=\"{prev}\" hx-swap-oob=\"delete\"></div>";
                html = deleteFragment + cardHtml;
            }
            else
            {
                html = cardHtml;
            }

            // Highlight demote: the previous crown lost its ribbon. Re-render that
            // card as an out-of-band outerHTML swap so the browser drops the trophy
            // without us needing to track per-card state on the client. Skip when
            // the demoted card is the same DOM node we just rendered above or the
            // one we OOB-deleted — both already reflect the current state.
            var demoted = broadcast.HighlightChange?.Demoted;
            if (demoted is not null
                && demoted.DomId != entry.DomId
                && demoted.DomId != prev)
            {
                var demotedHtml = await RenderComponentToString<LootFeedItem>(
                    serviceProvider, loggerFactory,
                    new Dictionary<string, object?>
                    {
                        ["Entry"] = demoted,
                        ["Animate"] = false,
                        ["IsMerge"] = true // forces hx-swap-oob="outerHTML" on the rendered root
                    });
                html = demotedHtml + html;
            }

            var ssePayload = string.Join("\n", html.Split('\n').Select(line => $"data: {line}"));
            await context.Response.WriteAsync($"{ssePayload}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }

    private static async Task<string> RenderComponentToString<TComponent>(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IDictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(serviceProvider, loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(
                ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
}
