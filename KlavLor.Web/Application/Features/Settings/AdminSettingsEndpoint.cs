using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.CollectionLog;
using KlavLor.Application.Features.DropRates;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Leaderboard;
using KlavLor.Application.Features.Loot.Special;
using KlavLor.Application.Features.Loot.SourceModels;
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

        // Bulk resync kick-off — reads a checkbox from the form, so antiforgery is
        // disabled (admin-gated, SameSite=Strict cookie, same as the rename endpoint).
        app.MapPost(AppRoutes.AdminDropRatesResyncStart.FromApi(), StartResync)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        // One source per call, chained from the browser. Uses "position" (300/min per
        // user) rather than "mutation" (60/min): the chain is strictly sequential and
        // admin-only, but a large backlog would blow past 60/min and stall mid-run.
        app.MapPost(AppRoutes.AdminDropRatesResyncStep.FromApi(), StepResync)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("position")
            .DisableAntiforgery();

        app.MapGet(AppRoutes.AdminDropRatesMismatches.FromApi(), GetDropRateMismatches)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapGet(AppRoutes.AdminLeaderboardExclusionSearch.FromApi(), SearchLeaderboardExclusions)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminLeaderboardExclusionExclude.FromApi(), ExcludeLeaderboardSource)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapPost(AppRoutes.AdminLeaderboardExclusionInclude.FromApi(), IncludeLeaderboardSource)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminLeaderboardItemExclusionSearch.FromApi(), SearchLeaderboardItemExclusions)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminLeaderboardItemExclusionExclude.FromApi(), ExcludeLeaderboardItem)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapPost(AppRoutes.AdminLeaderboardItemExclusionInclude.FromApi(), IncludeLeaderboardItem)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSourceModifierSearch.FromApi(), SearchSourceModifiers)
            .RequireAuthorization(nameof(RoleName.Admin));

        // Apply reads source/item/multiplier from form fields (dynamic), so antiforgery is
        // disabled — admin-gated, rate-limited, SameSite=Strict cookie (same as the rename path).
        app.MapPost(AppRoutes.AdminSourceModifierApply.FromApi(), ApplySourceModifier)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapPost(AppRoutes.AdminSourceModifierRemove.FromApi(), RemoveSourceModifier)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSpecialLoot.FromApi(), GetSpecialLoot)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapGet(AppRoutes.AdminSpecialLootItemSearch.FromApi(), SearchSpecialLootItems)
            .RequireAuthorization(nameof(RoleName.Admin));

        // Inject reads all fields from the form (dynamic), so antiforgery is disabled — admin-gated,
        // rate-limited, SameSite=Strict cookie (same as the other form-field admin posts).
        app.MapPost(AppRoutes.AdminSpecialLootInject.FromApi(), InjectSpecialLoot)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapGet(AppRoutes.AdminIcons.FromApi(), GetIcons)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminIconsRetry.FromApi(), RetryIcon)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSyncStatus.FromApi(), GetSyncStatus)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapGet(AppRoutes.AdminJobHealth.FromApi(), GetJobHealth)
            .RequireAuthorization(nameof(RoleName.Admin));

        app.MapPost(AppRoutes.AdminClogSyncNow.FromApi(), ClogSyncNow)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSourceSearch.FromApi(), SearchSources)
            .RequireAuthorization(nameof(RoleName.Admin));

        // Read-only impact preview shown before a rename/merge is committed.
        app.MapGet(AppRoutes.AdminSourceRenamePreview.FromApi(), PreviewRename)
            .RequireAuthorization(nameof(RoleName.Admin));

        // Re-renders a single source row (used to cancel out of the preview).
        app.MapGet(AppRoutes.AdminSourceRow.FromApi(), GetSourceRow)
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
        // Nullable so an absent param binds to null instead of 400. The "Show sources with no
        // wiki data" checkbox is unchecked by default, and an unchecked checkbox submits no
        // value at all — so the search box's hx-include omits showNoData on the initial load.
        [FromQuery] bool? showNoData,
        DropRateAdminHandler handler)
    {
        var items = await handler.Search(searchTerm, showNoData ?? false);
        return IResultExtensions.Component<DropRateResults>(new { Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> SyncDropRates(
        [FromQuery] string source,
        DropRateAdminHandler handler)
    {
        var row = await handler.Sync(source);
        return IResultExtensions.Component<DropRateRow>(new { Item = row });
    }

    private static async Task<RazorComponentResult> StartResync(
        // Unchecked checkbox submits nothing, so bind nullable → false rather than 400.
        [FromForm] bool? includeNoData,
        DropRateAdminHandler handler)
    {
        var sources = await handler.GetResyncBacklog(includeNoData ?? false);
        return IResultExtensions.Component<DropRateResyncPanel>(new
        {
            Sources = sources,
            IncludeNoData = includeNoData ?? false
        });
    }

    private static async Task<RazorComponentResult> StepResync(
        [FromForm] string queue,
        [FromForm] int stored,
        [FromForm] int noData,
        [FromForm] int failed,
        [FromForm] int total,
        DropRateAdminHandler handler)
    {
        var pending = JsonSerializer.Deserialize<List<string>>(queue) ?? [];
        if (pending.Count == 0)
        {
            // Defensive — the chain stops itself when the queue empties, so this only
            // fires on a tampered/replayed request. Emit the summary and stop.
            return IResultExtensions.Component<DropRateResyncStep>(new
            {
                Completed = new DropRateSourceRow(string.Empty, 0, null),
                Remaining = (IReadOnlyList<string>)Array.Empty<string>(),
                Stored = stored,
                NoData = noData,
                Failed = failed,
                Total = total
            });
        }

        var head = pending[0];
        var remaining = pending.GetRange(1, pending.Count - 1);
        var (row, outcome) = await handler.SyncWithOutcome(head);

        switch (outcome)
        {
            case DropRateSyncOutcome.Synced: stored++; break;
            case DropRateSyncOutcome.NoData: noData++; break;
            default: failed++; break;
        }

        return IResultExtensions.Component<DropRateResyncStep>(new
        {
            Completed = row,
            Remaining = (IReadOnlyList<string>)remaining,
            Stored = stored,
            NoData = noData,
            Failed = failed,
            Total = total
        });
    }

    private static async Task<RazorComponentResult> GetDropRateMismatches(DropRateAdminHandler handler)
    {
        var (items, total) = await handler.GetMissingRates();
        return IResultExtensions.Component<ClogMissingRatesPanel>(new { Items = items, Total = total });
    }

    private static async Task<RazorComponentResult> SearchLeaderboardExclusions(
        [FromQuery] string? searchTerm,
        LeaderboardExclusionAdminHandler handler)
    {
        var items = await handler.Search(searchTerm);
        return IResultExtensions.Component<LeaderboardExclusionResults>(new { Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> ExcludeLeaderboardSource(
        [FromQuery] string source,
        [FromQuery] long count,
        [FromQuery] int rowIndex,
        LeaderboardExclusionAdminHandler handler)
    {
        var row = await handler.Exclude(source, count);
        return IResultExtensions.Component<LeaderboardExclusionRow>(new { Item = row, RowIndex = rowIndex });
    }

    private static async Task<RazorComponentResult> IncludeLeaderboardSource(
        [FromQuery] string source,
        [FromQuery] long count,
        [FromQuery] int rowIndex,
        LeaderboardExclusionAdminHandler handler)
    {
        var row = await handler.Include(source, count);
        return IResultExtensions.Component<LeaderboardExclusionRow>(new { Item = row, RowIndex = rowIndex });
    }

    private static async Task<RazorComponentResult> SearchLeaderboardItemExclusions(
        [FromQuery] string? searchTerm,
        LeaderboardItemExclusionAdminHandler handler)
    {
        var items = await handler.Search(searchTerm);
        return IResultExtensions.Component<LeaderboardItemExclusionResults>(new { Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> ExcludeLeaderboardItem(
        [FromQuery] string item,
        [FromQuery] long count,
        [FromQuery] int rowIndex,
        LeaderboardItemExclusionAdminHandler handler)
    {
        var row = await handler.Exclude(item, count);
        return IResultExtensions.Component<LeaderboardItemExclusionRow>(new { Item = row, RowIndex = rowIndex });
    }

    private static async Task<RazorComponentResult> IncludeLeaderboardItem(
        [FromQuery] string item,
        [FromQuery] long count,
        [FromQuery] int rowIndex,
        LeaderboardItemExclusionAdminHandler handler)
    {
        var row = await handler.Include(item, count);
        return IResultExtensions.Component<LeaderboardItemExclusionRow>(new { Item = row, RowIndex = rowIndex });
    }

    private static async Task<RazorComponentResult> SearchSourceModifiers(
        [FromQuery] string? searchTerm,
        SourceRateModifierAdminHandler handler)
    {
        var items = await handler.Search(searchTerm);
        return IResultExtensions.Component<SourceRateModifierResults>(new
        {
            Items = items,
            IsSearch = !string.IsNullOrWhiteSpace(searchTerm)
        });
    }

    private static async Task<RazorComponentResult> ApplySourceModifier(
        [FromForm] string sourceName,
        [FromForm] string? itemName,
        [FromForm] double multiplier,
        SourceRateModifierAdminHandler handler)
    {
        var items = await handler.Apply(sourceName, itemName, multiplier);
        return IResultExtensions.Component<SourceRateModifierResults>(new { Items = items, IsSearch = false });
    }

    private static async Task<RazorComponentResult> RemoveSourceModifier(
        [FromQuery] string source,
        [FromQuery] string item,
        SourceRateModifierAdminHandler handler)
    {
        var items = await handler.Remove(source, item);
        return IResultExtensions.Component<SourceRateModifierResults>(new { Items = items, IsSearch = false });
    }

    private static async Task<RazorComponentResult> GetSpecialLoot(SpecialLootHandler handler)
    {
        var characters = await handler.GetCharacters();
        return IResultExtensions.Component<SpecialLootPanel>(new { Characters = characters });
    }

    private static async Task<RazorComponentResult> SearchSpecialLootItems(
        [FromQuery] string? searchTerm,
        SpecialLootHandler handler)
    {
        var items = await handler.SearchItems(searchTerm);
        return IResultExtensions.Component<SpecialLootItemResults>(new { Items = items });
    }

    private static async Task<RazorComponentResult> InjectSpecialLoot(
        [FromForm] int characterId,
        [FromForm] string itemName,
        [FromForm] string sourceName,
        [FromForm] string? occurredAt,
        [FromForm] bool? announce,
        SpecialLootHandler handler)
    {
        if (!DateTime.TryParse(occurredAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var localDt))
        {
            return IResultExtensions.Component<SpecialLootStatus>(new
            {
                Success = false,
                Message = "Enter a valid date and time."
            });
        }

        var when = IngestTimezone.FromLocalNaive(localDt);
        var result = await handler.Inject(characterId, itemName, sourceName, when, announce ?? false);

        return IResultExtensions.Component<SpecialLootStatus>(new
        {
            Success = result.IsSuccess,
            Message = result.IsSuccess
                ? $"Added {itemName} to the character's log."
                : (string.IsNullOrEmpty(result.ErrorMessage) ? "Failed to add special drop." : result.ErrorMessage)
        });
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

    private static async Task<RazorComponentResult> GetJobHealth(
        [FromQuery] string? expand,
        JobHealthHandler handler)
    {
        var rows = await handler.GetHealth();

        // expand=<jobName> renders the full-width single-job accordion view; otherwise the grid.
        if (!string.IsNullOrEmpty(expand))
        {
            var row = rows.FirstOrDefault(r => r.JobName == expand);
            if (row is not null)
            {
                var runs = await handler.GetHistory(expand);
                return IResultExtensions.Component<JobHealthExpanded>(new { Row = row, Runs = runs });
            }
        }

        return IResultExtensions.Component<JobHealthPanel>(new { Rows = rows });
    }

    private static async Task<RazorComponentResult> SearchSources(
        [FromQuery] string? searchTerm,
        SourceAdminHandler handler)
    {
        var items = await handler.Search(searchTerm);
        return IResultExtensions.Component<SourceNamesPanel>(new { Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> PreviewRename(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] int rowIndex,
        [FromQuery] long count,
        SourceAdminHandler handler)
    {
        var preview = await handler.Preview(from, to);
        return IResultExtensions.Component<SourceRenamePreviewRow>(new
        {
            Preview = preview,
            RowIndex = rowIndex,
            LootCount = count
        });
    }

    private static RazorComponentResult GetSourceRow(
        [FromQuery] string name,
        [FromQuery] long count,
        [FromQuery] int rowIndex)
        => IResultExtensions.Component<SourceNameRowItem>(new
        {
            Item = new SourceNameRow(name, count),
            RowIndex = rowIndex
        });

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
