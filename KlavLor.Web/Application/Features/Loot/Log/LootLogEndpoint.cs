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

        app.MapGet(AppRoutes.LootLogSource.FromApi(), GetSourceDetail)
            .AllowAnonymous()
            .AddEndpointFilter<HtmxNavigationFilter>();

        return app.MapGet(AppRoutes.LootLogSourceSession.FromApi(), GetSessionKills)
            .AllowAnonymous();
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
        LootLogHandler handler = default!,
        LootCharacterProfileHandler profileHandler = default!)
    {
        // Page 2+ is session pagination — just append the next batch of session cards.
        if (pageNumber > 1)
        {
            var more = await handler.HandleSourceSessions(id, name, pageNumber);
            return IResultExtensions.Component<LootLogSessionMore>(new
            {
                Sessions = more.Value,
                CharacterId = id,
                PageNumber = pageNumber
            });
        }

        // Sequential awaits — scoped DbContext is not safe under concurrent use.
        var detail = await handler.HandleSource(id, name);

        // Only build the active tab's data (see LootLogSourceDetail): the Kill Sessions view needs
        // sessions, every other view needs the collection. Building both made each tab pay for the
        // heavy collection aggregate (~5s on large sources) even when it isn't rendered.
        var isKills = string.Equals(view, "kills", StringComparison.OrdinalIgnoreCase);
        var sessions = isKills ? await handler.HandleSourceSessions(id, name) : null;
        var collection = !isKills ? await profileHandler.HandleSourceCollection(id, name) : null;

        return IResultExtensions.Component<LootLogSourceDetail>(new
        {
            Detail = detail.Value,
            Sessions = sessions?.Value,
            Collection = collection?.Value,
            View = view,
            CharacterId = id
        });
    }

    private static async Task<RazorComponentResult> GetSessionKills(
        int id,
        [FromQuery] string name,
        [FromQuery] int session,
        LootLogHandler handler)
    {
        var result = await handler.HandleSessionKills(id, name, session);
        return IResultExtensions.Component<SourceSessionModal>(new
        {
            SourceName = name,
            Kills = result.Value ?? []
        });
    }
}
