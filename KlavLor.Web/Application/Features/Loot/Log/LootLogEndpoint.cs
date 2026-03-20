using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Loot.Log;

public sealed class LootLogEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.LootLog.FromApi(), GetUsers)
            .AllowAnonymous();

        app.MapGet(AppRoutes.LootLogUser.FromApi(), GetUserLog)
            .AllowAnonymous();

        return app.MapGet(AppRoutes.LootLogSource.FromApi(), GetSourceDetail)
            .AllowAnonymous();
    }

    private static async Task<RazorComponentResult> GetUsers(LootLogHandler handler)
    {
        var result = await handler.HandleUsers();
        return IResultExtensions.Component<LootLogUsersGrid>(new { Users = result.Value });
    }

    private static async Task<RazorComponentResult> GetUserLog(
        int id,
        [AsParameters] LootLogQuery query,
        LootLogHandler handler)
    {
        var result = await handler.Handle(id, query);

        if (query.PageNumber > 1)
            return IResultExtensions.Component<LootLogMoreCards>(new
            {
                Result = result.Value,
                UserId = id,
                PageNumber = query.PageNumber
            });

        return IResultExtensions.Component<LootLogGrid>(new
        {
            Result = result.Value,
            UserId = id
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
                UserId = id,
                PageNumber = pageNumber,
                PageSize = pageSize
            });

        return IResultExtensions.Component<LootLogSourceDetail>(new
        {
            Detail = result.Value,
            UserId = id,
            PageSize = pageSize
        });
    }
}
