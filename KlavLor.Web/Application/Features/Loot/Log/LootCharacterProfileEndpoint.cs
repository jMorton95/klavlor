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

        app.MapGet(AppRoutes.LootLogCharacterMonthly.FromApi(), GetMonthly)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterRecords.FromApi(), GetRecords)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterTopItems.FromApi(), GetTopItems)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterRecentFirsts.FromApi(), GetRecentFirsts)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterSessions.FromApi(), GetSessions)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterSources.FromApi(), GetCharacterSources)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>();

        app.MapGet(AppRoutes.LootLogSourceCollection.FromApi(), GetSourceCollection)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacterFirsts.FromApi(), GetFirstTimeFeed)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>();

        return app.MapGet(AppRoutes.LootLogCharacterDay.FromApi(), GetDayFeed)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>();
    }

    private static async Task<IResult> GetSessions(
        int id,
        [FromQuery] int? pageNumber,
        LootCharacterProfileHandler handler)
    {
        var page = pageNumber is null or < 1 ? 1 : pageNumber.Value;
        var result = await handler.HandleCharacterSessions(id, page);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();

        // Page 1 renders the whole panel (heading + section); "load more" re-renders just the
        // day-grouped section (cumulative), swapping #char-session-section.
        if (page > 1)
            return IResultExtensions.Component<CharacterSessionList>(new
            {
                History = result.Value,
                CharacterId = id,
                PageNumber = page
            });

        return IResultExtensions.Component<CharacterSessionHistoryPanel>(new
        {
            History = result.Value,
            CharacterId = id
        });
    }

    private static async Task<IResult> GetCharacterSources(
        int id,
        [AsParameters] LootLogQuery query,
        LootLogHandler logHandler,
        LootCharacterProfileHandler profileHandler)
    {
        var result = await logHandler.HandleSourceTable(id, query);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();
        var table = result.Value!;

        // Page 2+ appends rows; a sort/filter request re-renders just the table; a fresh
        // navigation returns the full page content (back link + table).
        if (query.PageNumber > 1)
            return IResultExtensions.Component<SourceTableRows>(new
            {
                Table = table,
                CharacterId = id,
                PageNumber = query.PageNumber
            });

        if (query.SortBy is not null || !string.IsNullOrWhiteSpace(query.SearchTerm))
            return IResultExtensions.Component<SourceTableGrid>(new { Table = table, CharacterId = id });

        var header = await profileHandler.HandleHeader(id);
        return IResultExtensions.Component<CharacterSourcesContent>(new
        {
            Table = table,
            CharacterId = id,
            CharacterName = header.IsSuccess ? header.Value!.CharacterName : null
        });
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

    private static async Task<IResult> GetMonthly(
        int id,
        [FromQuery] string? range,
        LootCharacterProfileHandler handler)
    {
        var normalised = string.Equals(range, "all", StringComparison.OrdinalIgnoreCase) ? "all" : "12m";
        var result = await handler.HandleMonthlyTrend(id, normalised);
        if (!result.IsSuccess) return Microsoft.AspNetCore.Http.Results.NotFound();
        return IResultExtensions.Component<ProfileMonthlyTrend>(new
        {
            Trend = result.Value,
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
