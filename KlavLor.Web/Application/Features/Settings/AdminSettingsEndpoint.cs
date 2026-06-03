using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.CollectionLog;
using KlavLor.Application.Features.DropRates;
using KlavLor.Application.Features.Settings;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.Results;

namespace KlavLor.Web.Application.Features.Settings;

public sealed class AdminSettingsEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.AdminSettings.FromApi(), GetHub)
            .RequireAuthorization(nameof(RoleName.Admin))
            .AddEndpointFilter<HtmxNavigationFilter>();

        app.MapPost(AppRoutes.AdminSettingsLeaguesToggle.FromApi(), ToggleLeagues)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminClogSearch.FromApi(), SearchClog)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminClogExclude.FromApi(), ExcludeClog)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapPost(AppRoutes.AdminClogInclude.FromApi(), IncludeClog)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminDropRatesSearch.FromApi(), SearchDropRates)
            .RequireAuthorization(nameof(RoleName.Admin));

        return app.MapPost(AppRoutes.AdminDropRatesSync.FromApi(), SyncDropRates)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");
    }

    private static RazorComponentResult GetHub()
        => IResultExtensions.Component<AdminSettingsHub>();

    private static async Task<RazorComponentResult> ToggleLeagues(
        SystemSettingsHandler handler,
        ISystemSettingsCache cache)
    {
        await handler.HandleToggleLeagues();
        return IResultExtensions.Component<AdminSettingsToggleLeagues>(new
        {
            IsLeaguesEnabled = cache.IsLeaguesEnabled
        });
    }

    private static async Task<RazorComponentResult> SearchClog(
        [FromQuery] string? searchTerm,
        CollectionLogAdminHandler handler)
    {
        var items = await handler.Search(searchTerm);
        return IResultExtensions.Component<ClogResults>(new { Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> ExcludeClog(
        [FromQuery] int itemId,
        [FromQuery] string itemName,
        CollectionLogAdminHandler handler)
    {
        var row = await handler.Exclude(itemId, itemName);
        return IResultExtensions.Component<ClogResultRow>(new { Item = row });
    }

    private static async Task<RazorComponentResult> IncludeClog(
        [FromQuery] int itemId,
        [FromQuery] string itemName,
        CollectionLogAdminHandler handler)
    {
        var row = await handler.Include(itemId, itemName);
        return IResultExtensions.Component<ClogResultRow>(new { Item = row });
    }

    private static async Task<RazorComponentResult> SearchDropRates(
        [FromQuery] string? searchTerm,
        DropRateAdminHandler handler)
    {
        var items = await handler.Search(searchTerm);
        return IResultExtensions.Component<DropRateResults>(new { Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> SyncDropRates(
        [FromQuery] string source,
        DropRateAdminHandler handler)
    {
        var row = await handler.Sync(source);
        return IResultExtensions.Component<DropRateRow>(new { Item = row });
    }
}
