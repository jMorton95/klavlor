using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Web.Application.Results;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KlavLor.Web.Application.Features.Loot.Feed;

public sealed class LootFeedEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.LootFeed.FromApi(), GetPage)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootFeedGrid.FromApi(), GetGrid)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootFeedStreamStandard.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, LootFeedTier.Standard))
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootFeedStreamUncommon.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, LootFeedTier.Uncommon))
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootFeedStreamRare.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, LootFeedTier.Rare))
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootFeedStreamEpic.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, LootFeedTier.Epic))
            .AllowAnonymous();

        return app.MapGet(AppRoutes.LootFeedStreamLegendary.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, LootFeedTier.Legendary))
            .AllowAnonymous();
    }

    private static async Task<IResult> GetPage(ILootLogRepository lootLogRepository)
    {
        var tierData = await lootLogRepository.GetAllFeedTiers(50);

        return IResultExtensions.Component<LootFeedContent>(new
        {
            StandardEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Standard],
            UncommonEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Uncommon],
            RareEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Rare],
            EpicEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Epic],
            LegendaryEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Legendary]
        });
    }

    private static async Task<IResult> GetGrid(ILootLogRepository lootLogRepository, string? tiers = null)
    {
        var requestedTiers = ParseTiers(tiers);
        var tierData = await lootLogRepository.GetAllFeedTiers(50, requestedTiers);

        return IResultExtensions.Component<LootFeedGrid>(new
        {
            StandardEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Standard],
            UncommonEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Uncommon],
            RareEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Rare],
            EpicEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Epic],
            LegendaryEntries = (IReadOnlyList<LootFeedEntry>)tierData[LootFeedTier.Legendary],
            ActiveTiers = requestedTiers is not null
                ? (IReadOnlyList<LootFeedTier>)requestedTiers.Order().ToList()
                : (IReadOnlyList<LootFeedTier>)ILootFeedService.AllTiers
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
        LootFeedTier tier)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        var ct = context.RequestAborted;

        await foreach (var broadcast in feedService.SubscribeAsync(tier, ct))
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
