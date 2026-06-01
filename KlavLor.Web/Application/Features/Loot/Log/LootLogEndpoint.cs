using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Web.Application.Features.Loot.Log.Profile;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Loot.Log;

public sealed class LootLogEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.LootLog.FromApi(), GetCharacters)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogCharacter.FromApi(), GetCharacterLog)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>();

        return app.MapGet(AppRoutes.LootLogSource.FromApi(), GetSourceDetail)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>();
    }

    private static async Task<RazorComponentResult> GetCharacters(LootLogHandler handler)
    {
        var result = await handler.HandleCharacters();
        return IResultExtensions.Component<LootLogUsersGrid>(new { Characters = result.Value });
    }

    private static async Task<RazorComponentResult> GetCharacterLog(
        int id,
        [AsParameters] LootLogQuery query,
        LootLogHandler handler,
        LootCharacterProfileHandler profileHandler)
    {
        // Sequential awaits — scoped DbContext is not safe under concurrent use.
        var searchResult = await handler.Handle(id, query);

        // Pagination requests (page > 1 or search refinements) only need the source grid.
        if (query.PageNumber > 1)
        {
            return IResultExtensions.Component<LootLogMoreCards>(new
            {
                Result = searchResult.Value,
                CharacterId = id,
                PageNumber = query.PageNumber
            });
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            return IResultExtensions.Component<LootLogGrid>(new
            {
                Result = searchResult.Value,
                CharacterId = id
            });
        }

        // Fresh profile view: eager-fetch header + window stats.
        var header = await profileHandler.HandleHeader(id);
        var windows = await profileHandler.HandleWindowStats(id);

        return IResultExtensions.Component<CharacterProfile>(new
        {
            CharacterId = id,
            Header = header.Value,
            Windows = windows.Value,
            SearchResult = searchResult.Value
        });
    }

    private static async Task<RazorComponentResult> GetSourceDetail(
        int id,
        [FromQuery] string name,
        [FromQuery] string? view = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        LootLogHandler handler = default!,
        LootCharacterProfileHandler profileHandler = default!)
    {
        var result = await handler.HandleSource(id, name, pageNumber, pageSize);

        if (pageNumber > 1)
            return IResultExtensions.Component<LootLogSourceMoreKills>(new
            {
                Detail = result.Value,
                CharacterId = id,
                PageNumber = pageNumber,
                PageSize = pageSize
            });

        // Sequential awaits — scoped DbContext is not safe under concurrent use.
        var collection = await profileHandler.HandleSourceCollection(id, name);

        return IResultExtensions.Component<LootLogSourceDetail>(new
        {
            Detail = result.Value,
            Collection = collection.Value,
            View = view,
            CharacterId = id,
            PageSize = pageSize
        });
    }
}
