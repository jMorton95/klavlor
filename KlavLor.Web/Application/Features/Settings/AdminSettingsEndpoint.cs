using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.CollectionLog;
using KlavLor.Application.Features.DropRates;
using KlavLor.Application.Features.Maintenance;
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

        app.MapPost(AppRoutes.AdminDropRatesSync.FromApi(), SyncDropRates)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminDropRatesMismatches.FromApi(), GetDropRateMismatches)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapGet(AppRoutes.AdminIcons.FromApi(), GetIcons)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminIconsRetry.FromApi(), RetryIcon)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSyncStatus.FromApi(), GetSyncStatus)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminClogSyncNow.FromApi(), ClogSyncNow)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSourceSearch.FromApi(), SearchSources)
            .RequireAuthorization(nameof(RoleName.Admin));

        // Rename takes the new name from a form field (dynamic), so antiforgery is disabled;
        // it's admin-gated, rate-limited, and the auth cookie is SameSite=Strict.
        return app.MapPost(AppRoutes.AdminSourceRename.FromApi(), RenameSource)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();
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
        [FromQuery] bool showNoData,
        DropRateAdminHandler handler)
    {
        var items = await handler.Search(searchTerm, showNoData);
        return IResultExtensions.Component<DropRateResults>(new { Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> SyncDropRates(
        [FromQuery] string source,
        DropRateAdminHandler handler)
    {
        var row = await handler.Sync(source);
        return IResultExtensions.Component<DropRateRow>(new { Item = row });
    }

    private static async Task<RazorComponentResult> GetDropRateMismatches(DropRateAdminHandler handler)
    {
        var (items, total) = await handler.GetMissingRates();
        return IResultExtensions.Component<ClogMissingRatesPanel>(new { Items = items, Total = total });
    }

    private static async Task<RazorComponentResult> GetIcons(IconAuditHandler handler)
    {
        var icons = await handler.GetFailed();
        return IResultExtensions.Component<FailedIconsPanel>(new { Icons = icons });
    }

    private static async Task<RazorComponentResult> RetryIcon(
        [FromQuery] IconKind kind,
        [FromQuery] int id,
        [FromQuery] string name,
        IconAuditHandler handler)
    {
        await handler.Retry(kind, id);
        return IResultExtensions.Component<FailedIconRow>(new
        {
            Icon = new FailedIcon(kind, id, name, 0, null),
            Queued = true
        });
    }

    private static async Task<RazorComponentResult> GetSyncStatus(SyncStatusHandler handler)
    {
        var status = await handler.Get();
        return IResultExtensions.Component<SyncStatusPanel>(new { Status = status });
    }

    private static async Task<RazorComponentResult> ClogSyncNow(SyncStatusHandler handler)
    {
        var status = await handler.RunClogSyncNow();
        return IResultExtensions.Component<SyncStatusPanel>(new { Status = status });
    }

    private static async Task<RazorComponentResult> SearchSources(
        [FromQuery] string? searchTerm,
        SourceAdminHandler handler)
    {
        var items = await handler.Search(searchTerm);
        return IResultExtensions.Component<SourceNamesPanel>(new { Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> RenameSource(
        [FromQuery] string from,
        [FromForm] string to,
        [FromQuery] int rowIndex,
        SourceAdminHandler handler)
    {
        var result = await handler.Rename(from, to);
        return IResultExtensions.Component<SourceRenameRow>(new { Result = result, RowIndex = rowIndex });
    }
}
