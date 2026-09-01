using System.Runtime.CompilerServices;
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
        // Main feed routes. The page route is deliberately handler-free — see GetPage.
        app.MapGet(AppRoutes.LootFeed.FromApi(), () => GetPage(LootFeedScope.Main))
            .AllowAnonymous()
            .RequireRateLimiting("read");

        // Grid = swimlane shells only, no loot and no query. Each shell then fetches its own tier.
        app.MapGet(AppRoutes.LootFeedGrid.FromApi(), (LootFeedTiersHandler handler, string? tiers) => GetGrid(handler, LootFeedScope.Main, tiers))
            .AllowAnonymous()
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.LootFeedColumn.FromApi(), (string tier, int? cols, int? characterId, LootFeedTiersHandler handler) =>
                GetColumn(handler, LootFeedScope.Main, tier, cols, characterId))
            .AllowAnonymous()
            .RequireRateLimiting("read");

        // Recent activity: what everyone has actually been grinding, which drop cards can't show.
        // Loaded on demand when the popover is first opened rather than with the feed.
        app.MapGet(AppRoutes.LootFeedSessions.FromApi(), (RecentSessionsHandler handler) =>
                GetRecentSessions(handler, LootFeedScope.Main))
            .AllowAnonymous()
            .RequireRateLimiting("read");

        MapStream(app, AppRoutes.LootFeedStreamStandard, LootFeedScope.Main, LootFeedTier.Standard);
        MapStream(app, AppRoutes.LootFeedStreamUncommon, LootFeedScope.Main, LootFeedTier.Uncommon);
        MapStream(app, AppRoutes.LootFeedStreamRare, LootFeedScope.Main, LootFeedTier.Rare);
        MapStream(app, AppRoutes.LootFeedStreamEpic, LootFeedScope.Main, LootFeedTier.Epic);
        MapStream(app, AppRoutes.LootFeedStreamLegendary, LootFeedScope.Main, LootFeedTier.Legendary);

        // Leagues feed routes — parallel set, filters to IsLeagues=true characters.
        // All Leagues endpoints short-circuit to 404 when the admin has disabled the feature.
        app.MapGet(AppRoutes.LootFeedLeagues.FromApi(), IResult (ISystemSettingsCache settings) =>
                settings.IsLeaguesEnabled
                    ? GetPage(LootFeedScope.Leagues)
                    : TypedResults.NotFound())
            .AllowAnonymous()
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.LootFeedLeaguesGrid.FromApi(), async (ISystemSettingsCache settings, LootFeedTiersHandler handler, string? tiers) =>
                settings.IsLeaguesEnabled
                    ? await GetGrid(handler, LootFeedScope.Leagues, tiers)
                    : Results.NotFound())
            .AllowAnonymous()
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.LootFeedLeaguesColumn.FromApi(), async (string tier, int? cols, int? characterId, LootFeedTiersHandler handler, ISystemSettingsCache settings) =>
            {
                if (!settings.IsLeaguesEnabled) return Results.NotFound();
                return await GetColumn(handler, LootFeedScope.Leagues, tier, cols, characterId);
            })
            .AllowAnonymous()
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.LootFeedLeaguesSessions.FromApi(), async (RecentSessionsHandler handler, ISystemSettingsCache settings) =>
            {
                if (!settings.IsLeaguesEnabled) return TypedResults.NotFound();
                return await GetRecentSessions(handler, LootFeedScope.Leagues);
            })
            .AllowAnonymous()
            .RequireRateLimiting("read");

        // The roll ticker's own stream, one per scope. Same "sse" policy as the lanes.
        app.MapGet(AppRoutes.LootFeedStreamRolls.FromApi(),
                (HttpContext ctx, ILootRollFeed rolls, IServiceProvider sp, ILoggerFactory lf) =>
                    StreamRolls(ctx, rolls, LootFeedScope.Main, sp, lf))
            .AllowAnonymous()
            .RequireRateLimiting("sse");

        app.MapGet(AppRoutes.LootFeedLeaguesStreamRolls.FromApi(),
                async (HttpContext ctx, ILootRollFeed rolls, ISystemSettingsCache settings,
                       IServiceProvider sp, ILoggerFactory lf) =>
                {
                    if (!settings.IsLeaguesEnabled)
                    {
                        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }
                    await StreamRolls(ctx, rolls, LootFeedScope.Leagues, sp, lf);
                })
            .AllowAnonymous()
            .RequireRateLimiting("sse");

        MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamStandard, LootFeedTier.Standard);
        MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamUncommon, LootFeedTier.Uncommon);
        MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamRare, LootFeedTier.Rare);
        MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamEpic, LootFeedTier.Epic);
        return MapLeaguesStream(app, AppRoutes.LootFeedLeaguesStreamLegendary, LootFeedTier.Legendary);
    }

    // The "sse" policy is a per-IP *concurrency* limiter (see Program.cs): streams are held open
    // for the life of the page, so the limit that matters is simultaneous sockets per IP.
    private static RouteHandlerBuilder MapStream(IEndpointRouteBuilder app, string route, LootFeedScope scope, LootFeedTier tier) =>
        app.MapGet(route.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, scope, tier))
            .AllowAnonymous()
            .RequireRateLimiting("sse");

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
            .AllowAnonymous()
            .RequireRateLimiting("sse");

    // Synchronous and query-free on purpose: the feed shell is the one page that must paint
    // without waiting on the DB. LootFeedContent renders skeleton columns and an hx-trigger="load"
    // on #feed-grid-container, which then fetches GetGrid — that single response carries both the
    // backfill entries and the sse-connect attributes, so history and live streaming still arrive
    // together. Do not reintroduce a handler here.
    private static RazorComponentResult GetPage(LootFeedScope scope)
        => IResultExtensions.Component<LootFeedContent>(new { Scope = scope });

    // Lane shells, plus the character filter's options. Its job is to decide which lanes exist,
    // since the active-tier filter lives in the browser and arrives as ?tiers=. Each lane then
    // loads itself via GetColumn, so no request ever carries the whole grid.
    //
    // The character list rides along here rather than on a request of its own. This is the one
    // fetch that already fires on page load, and the character read is a projection of a small
    // indexed table — so the filter is populated on first paint without a second round trip, and
    // without putting a query back on the PAGE route, which must stay query-free.
    private static async Task<RazorComponentResult> GetGrid(LootFeedTiersHandler handler, LootFeedScope scope, string? tiers)
    {
        var requestedTiers = ParseTiers(tiers);
        var characters = await handler.GetCharacters(scope);
        return IResultExtensions.Component<LootFeedGrid>(new
        {
            ActiveTiers = requestedTiers is not null
                ? (IReadOnlyList<LootFeedTier>)requestedTiers.Order().ToList()
                : (IReadOnlyList<LootFeedTier>)ILootFeedService.AllTiers,
            Scope = scope,
            Characters = characters
        });
    }

    // One swimlane: its backfill entries and its SSE subscription, in a single response so history
    // and live streaming still arrive together.
    //
    // `cols` is the number of lanes the requesting shell laid out. The column replaces its own
    // shell via outerHTML, so it has to reproduce that width or the lane would resize as it lands —
    // and a single-tier response can't otherwise know how many siblings it has.
    private static async Task<IResult> GetColumn(
        LootFeedTiersHandler handler, LootFeedScope scope, string tier, int? cols, int? characterId)
    {
        if (!Enum.TryParse<LootFeedTier>(tier, ignoreCase: true, out var parsed))
            return Results.NotFound();

        // characterId arrives on the lane request, not the grid shell, because the lane is what
        // carries the backfill. site.js appends it to both from the same saved value.
        var tierData = await handler.Handle(scope, new HashSet<LootFeedTier> { parsed }, characterId);

        return IResultExtensions.Component<LootFeedColumn>(new
        {
            Tier = parsed,
            Scope = scope,
            Entries = (IReadOnlyList<LootFeedEntry>)tierData[parsed],
            ColumnClass = FeedColumnLayout.ColumnClass(
                Math.Clamp(cols ?? ILootFeedService.AllTiers.Length, 1, ILootFeedService.AllTiers.Length))
        });
    }

    private static async Task<IResult> GetRecentSessions(RecentSessionsHandler handler, LootFeedScope scope)
    {
        var panel = await handler.Handle(scope);
        return IResultExtensions.Component<RecentSessionsPopover>(new { Panel = panel, Scope = scope });
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

    /// <summary>
    /// The live roll ticker's stream. One chip of markup per kill.
    /// </summary>
    /// <remarks>
    /// NOT a Razor render, and that is the point. StreamFeed below spins up an HtmlRenderer per
    /// event PER SUBSCRIBER, so one publish costs N component renders with N viewers watching. A
    /// roll is a name, a source and a number, so it is built here by interpolation - which is what
    /// lets the ticker carry every kill rather than only the ones worth 10k.
    ///
    /// Both interpolated values are HTML-ENCODED. A character's display name is user-supplied and
    /// goes straight into markup; the feed's Razor path escapes for free, this one must do it.
    /// </remarks>
    private static async Task StreamRolls(
        HttpContext context, ILootRollFeed rolls, LootFeedScope scope,
        IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var ct = context.RequestAborted;

        // Same reason as StreamFeed: without an immediate write the response head is withheld until
        // the first kill lands, and the browser's EventSource sits in CONNECTING - indistinguishable
        // from a dead connection.
        await context.Response.WriteAsync(": connected\n\n", ct);
        await context.Response.Body.FlushAsync(ct);

        // Backfill, so the banner is populated the moment the page opens rather than blank until
        // the clan's next kill. It comes out of the service's in-memory ring, so unlike the
        // swimlanes' backfill it costs no query - which is what lets it live on the query-free
        // page shell.
        //
        // Replayed OLDEST FIRST because the banner prepends: the last one written ends up leftmost,
        // matching the order live rolls arrive in. GetRecent hands them back newest-first.
        foreach (var roll in rolls.GetRecent(scope).Reverse())
        {
            await context.Response.WriteAsync(
                $"{await RenderRollChip(roll, seeded: true, serviceProvider, loggerFactory)}\n\n", ct);
        }
        await context.Response.Body.FlushAsync(ct);

        // A roll published between the snapshot above and the subscription below is missed. That
        // window is microseconds and this is a ticker with no scrollback - nothing reads back
        // through it and nothing is derived from it - so it is not worth the interleaving the
        // swimlanes need, where a missed drop would be a hole in the history.
        await foreach (var roll in rolls.SubscribeAsync(scope, ct))
        {
            await context.Response.WriteAsync(
                $"{await RenderRollChip(roll, seeded: false, serviceProvider, loggerFactory)}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>
    /// The rendered chip for a roll, rendered ONCE however many people are watching.
    /// </summary>
    /// <remarks>
    /// StreamFeed builds an HtmlRenderer per event PER SUBSCRIBER, so one publish costs N renders
    /// with N viewers. The feed can carry that because it only publishes drops worth 10k or more;
    /// the ticker carries every kill, so the same arithmetic over a much larger event count is
    /// worth avoiding - and it is avoidable here for a reason the feed cannot use: a roll chip has
    /// no per-viewer state at all. No character filter, no merge, no highlight. Every subscriber
    /// gets byte-identical markup, so it only ever needs rendering once.
    ///
    /// ConditionalWeakTable rather than a dictionary so the cache needs no eviction of its own: an
    /// entry is reachable only while the service's ring buffer holds it, and the markup is collected
    /// with it. Two subscribers can race to render the same entry on its first appearance; they
    /// produce identical strings, so the loser is simply discarded.
    /// </remarks>
    private static readonly ConditionalWeakTable<LootRollEntry, string> RenderedChips = new();

    /// <summary>
    /// The same rolls again, marked as backfill so the banner does not animate them. Two tables
    /// rather than one because the same entry is legitimately both: it is streamed live as it
    /// lands, and then replayed out of the ring to whoever opens the page next. One cache would
    /// hand the second caller the first one's markup and the backfill would slide in after all.
    /// </summary>
    private static readonly ConditionalWeakTable<LootRollEntry, string> RenderedSeedChips = new();

    private static async Task<string> RenderRollChip(
        LootRollEntry roll, bool seeded, IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
    {
        var chips = seeded ? RenderedSeedChips : RenderedChips;
        if (chips.TryGetValue(roll, out var cached)) return cached;

        var html = await RenderComponentToString<LootRollChip>(
            serviceProvider, loggerFactory,
            new Dictionary<string, object?> { ["Roll"] = roll, ["Seeded"] = seeded });

        // Razor indents, so the markup is multi-line. An SSE frame is line-delimited: every line
        // needs its own "data: " prefix or the client sees a truncated fragment. Same treatment
        // StreamFeed gives its cards.
        var payload = string.Join("\n", html.Split('\n').Select(line => $"data: {line}"));

        chips.AddOrUpdate(roll, payload);
        return payload;
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
        // Disable proxy buffering (Traefik/nginx honour this) so events aren't held back.
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var ct = context.RequestAborted;

        // Flush an SSE comment immediately. Without it the response head isn't written until the
        // first drop is published, so the browser's EventSource sits in CONNECTING — indistinguishable
        // from a failed connection, and it can't fire onopen or detect a dead socket to retry. A
        // leading ":" line is a no-op the client ignores, so this only makes the subscription observable.
        await context.Response.WriteAsync(": connected\n\n", ct);
        await context.Response.Body.FlushAsync(ct);

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
