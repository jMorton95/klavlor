using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Web.Application.Features.Loot.Log.Profile;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Loot.Log;

public sealed class LootCharacterProfileEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.LootLogCharacterHeatmap.FromApi(), GetHeatmap)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterRecords.FromApi(), GetRecords)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterTopItems.FromApi(), GetTopItems)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterRecentFirsts.FromApi(), GetRecentFirsts)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogSourceCollection.FromApi(), GetSourceCollection)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterFirsts.FromApi(), GetFirstTimeFeed)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>();

        return app.MapGet(AppRoutes.LootLogCharacterDay.FromApi(), GetDayFeed)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>();
    }

    private static async Task<IResult> GetDayFeed(int id, string date, LootCharacterProfileHandler handler)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day))
            return Microsoft.AspNetCore.Http.Results.NotFound();

        var result = await handler.HandleDayFeed(id, day);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();

        return IResultExtensions.Component<CharacterDayFeedPage>(new
        {
            Feed = result.Value,
            CharacterId = id
        });
    }

    private static async Task<IResult> GetHeatmap(
        int id,
        [FromQuery] string? mode,
        LootCharacterProfileHandler handler)
    {
        var heatmapMode = string.Equals(mode, "clogs", StringComparison.OrdinalIgnoreCase)
            ? HeatmapMode.Clogs
            : HeatmapMode.Gp;
        var result = await handler.HandleHeatmap(id, heatmapMode);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();
        return IResultExtensions.Component<ProfileHeatmap>(new
        {
            Data = result.Value,
            CharacterId = id
        });
    }

    private static async Task<IResult> GetRecords(int id, LootCharacterProfileHandler handler)
    {
        var result = await handler.HandleRecords(id);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();
        return IResultExtensions.Component<ProfileRecords>(new
        {
            Records = result.Value,
            CharacterId = id
        });
    }

    private static async Task<IResult> GetTopItems(int id, LootCharacterProfileHandler handler)
    {
        var result = await handler.HandleTopItems(id, 10);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();
        return IResultExtensions.Component<ProfileTopItems>(new
        {
            Items = result.Value,
            CharacterId = id
        });
    }

    private static async Task<IResult> GetRecentFirsts(int id, LootCharacterProfileHandler handler)
    {
        var result = await handler.HandleFirstTimeFeed(id, before: null, pageSize: 12);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();
        return IResultExtensions.Component<ProfileRecentFirsts>(new
        {
            Feed = result.Value,
            CharacterId = id
        });
    }

    private static async Task<IResult> GetSourceCollection(
        int id,
        [FromQuery] string name,
        LootCharacterProfileHandler handler)
    {
        var result = await handler.HandleSourceCollection(id, name);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();
        return IResultExtensions.Component<SourceCollectionPanel>(new
        {
            Collection = result.Value,
            CharacterId = id
        });
    }

    private static async Task<IResult> GetFirstTimeFeed(
        int id,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int? pageSize,
        LootCharacterProfileHandler handler)
    {
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var result = await handler.HandleFirstTimeFeed(id, before, size);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();

        if (before is not null)
        {
            return IResultExtensions.Component<FirstTimeFeedAppend>(new
            {
                Feed = result.Value,
                CharacterId = id,
                PageSize = size
            });
        }

        return IResultExtensions.Component<FirstTimeFeedPage>(new
        {
            Feed = result.Value,
            CharacterId = id,
            PageSize = size
        });
    }
}
