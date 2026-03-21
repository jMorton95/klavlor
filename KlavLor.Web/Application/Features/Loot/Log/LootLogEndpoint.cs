using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Loot.Log;
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
        LootLogHandler handler)
    {
        var result = await handler.Handle(id, query);

        if (query.PageNumber > 1)
            return IResultExtensions.Component<LootLogMoreCards>(new
            {
                Result = result.Value,
                CharacterId = id,
                PageNumber = query.PageNumber
            });

        return IResultExtensions.Component<LootLogGrid>(new
        {
            Result = result.Value,
            CharacterId = id
        });
    }

    private static async Task<RazorComponentResult> GetSourceDetail(
        int id,
        [FromQuery] string name,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        LootLogHandler handler = default!)
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

        return IResultExtensions.Component<LootLogSourceDetail>(new
        {
            Detail = result.Value,
            CharacterId = id,
            PageSize = pageSize
        });
    }
}
