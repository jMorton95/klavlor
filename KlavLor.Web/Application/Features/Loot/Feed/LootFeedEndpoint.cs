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

        app.MapGet(AppRoutes.LootFeedStreamStandard.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, LootFeedTier.Standard))
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootFeedStreamNotable.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, LootFeedTier.Notable))
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootFeedStreamEpic.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, LootFeedTier.Epic))
            .AllowAnonymous();

        return app.MapGet(AppRoutes.LootFeedStreamLegendary.FromApi(), (HttpContext ctx, ILootFeedService svc, IServiceProvider sp, ILoggerFactory lf) =>
                StreamFeed(ctx, svc, sp, lf, LootFeedTier.Legendary))
            .AllowAnonymous();
    }

    private static IResult GetPage(ILootFeedService feedService)
    {
        var standard = feedService.GetCurrentEntries(LootFeedTier.Standard);
        var notable = feedService.GetCurrentEntries(LootFeedTier.Notable);
        var epic = feedService.GetCurrentEntries(LootFeedTier.Epic);
        var legendary = feedService.GetCurrentEntries(LootFeedTier.Legendary);

        return IResultExtensions.Component<LootFeedGrid>(new
        {
            StandardEntries = standard,
            NotableEntries = notable,
            EpicEntries = epic,
            LegendaryEntries = legendary
        });
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

        await foreach (var entry in feedService.SubscribeAsync(tier, ct))
        {
            var html = await RenderComponentToString<LootFeedItem>(
                serviceProvider, loggerFactory,
                new Dictionary<string, object?> { ["Entry"] = entry, ["Animate"] = true });

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
