using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using KlavLor.Application.Features.CollectionLog;
using KlavLor.Application.Features.DropRates;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Audit;
using KlavLor.Application.Features.Loot.Baseline;
using KlavLor.Application.Features.Loot.DelveDepth;
using KlavLor.Application.Features.Loot.ItemValues;
using KlavLor.Application.Features.Loot.Leaderboard;
using KlavLor.Application.Features.Loot.Special;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Features.Settings;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Shared;
using KlavLor.Web.Application.Filters;
using KlavLor.Web.Application.HttpResults;

namespace KlavLor.Web.Application.Features.Settings;

public sealed class AdminSettingsEndpoint : IEndpoint
{
    public static RouteHandlerBuilder MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(AppRoutes.AdminSettings.FromApi(), GetHub)
            .RequireAuthorization(nameof(RoleName.Admin))
            .AddEndpointFilter<HtmxNavigationFilter>()
            .RequireRateLimiting("read");

        // One section per URL. No HtmxNavigationFilter: it derives the push URL by stripping "/api"
        // from the request path, which is exactly right here, but the nav links already set
        // hx-push-url explicitly and a second HX-Push-Url header would just duplicate the work.
        app.MapGet(AppRoutes.AdminSettingsSection.FromApi(), GetHubSection)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapPost(AppRoutes.AdminSettingsLeaguesToggle.FromApi(), ToggleLeagues)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminClogSearch.FromApi(), SearchClog)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapPost(AppRoutes.AdminClogExclude.FromApi(), ExcludeClog)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapPost(AppRoutes.AdminClogInclude.FromApi(), IncludeClog)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminDropRatesSearch.FromApi(), SearchDropRates)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

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
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.AdminLeaderboardExclusionSearch.FromApi(), SearchLeaderboardExclusions)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapPost(AppRoutes.AdminLeaderboardExclusionExclude.FromApi(), ExcludeLeaderboardSource)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapPost(AppRoutes.AdminLeaderboardExclusionInclude.FromApi(), IncludeLeaderboardSource)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminLeaderboardItemExclusionSearch.FromApi(), SearchLeaderboardItemExclusions)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapPost(AppRoutes.AdminLeaderboardItemExclusionExclude.FromApi(), ExcludeLeaderboardItem)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapPost(AppRoutes.AdminLeaderboardItemExclusionInclude.FromApi(), IncludeLeaderboardItem)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSourceModifierSearch.FromApi(), SearchSourceModifiers)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        // Apply reads source/item/multiplier from form fields (dynamic), so antiforgery is
        // disabled — admin-gated, rate-limited, SameSite=Strict cookie (same as the rename path).
        app.MapPost(AppRoutes.AdminSourceModifierApply.FromApi(), ApplySourceModifier)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapPost(AppRoutes.AdminSourceModifierRemove.FromApi(), RemoveSourceModifier)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminRecordAudit.FromApi(), GetRecordAudit)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.AdminRecordAuditSources.FromApi(), GetRecordAuditSources)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.AdminRecordAuditSearch.FromApi(), SearchRecordAudit)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapDelete(AppRoutes.AdminRecordAuditDelete.FromApi(), DeleteAuditRecord)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapPost(AppRoutes.AdminRecordAuditExclude.FromApi(), SetAuditRecordLuckExclusion)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapGet(AppRoutes.AdminBaseline.FromApi(), GetBaselines)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapPost(AppRoutes.AdminBaselineSet.FromApi(), SetBaseline)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapGet(AppRoutes.AdminDelveDepth.FromApi(), GetDelveDepths)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapPost(AppRoutes.AdminDelveDepthSet.FromApi(), SetDelveDepth)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapDelete(AppRoutes.AdminDelveDepthRemove.FromApi(), RemoveDelveDepth)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapGet(AppRoutes.AdminItemValues.FromApi(), GetItemValues)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.AdminItemValueSearch.FromApi(), SearchItemValues)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        // Full scan of the drop table — only ever hit when an admin presses the button.
        app.MapGet(AppRoutes.AdminItemValueZeroReport.FromApi(), GetZeroValueItems)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        // Reads item id / name / value from form fields (dynamic), so antiforgery is disabled —
        // admin-gated, rate-limited, SameSite=Strict cookie (same as the other form-field posts).
        app.MapPost(AppRoutes.AdminItemValueSet.FromApi(), SetItemValue)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapPost(AppRoutes.AdminItemValueRemove.FromApi(), RemoveItemValue)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSpecialLoot.FromApi(), GetSpecialLoot)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.AdminSpecialLootItemSearch.FromApi(), SearchSpecialLootItems)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        // Inject reads all fields from the form (dynamic), so antiforgery is disabled — admin-gated,
        // rate-limited, SameSite=Strict cookie (same as the other form-field admin posts).
        app.MapPost(AppRoutes.AdminSpecialLootInject.FromApi(), InjectSpecialLoot)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();

        app.MapGet(AppRoutes.AdminIcons.FromApi(), GetIcons)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapPost(AppRoutes.AdminIconsRetry.FromApi(), RetryIcon)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSyncStatus.FromApi(), GetSyncStatus)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapGet(AppRoutes.AdminJobHealth.FromApi(), GetJobHealth)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        app.MapPost(AppRoutes.AdminJobRunNow.FromApi(), RunJobNow)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapPost(AppRoutes.AdminClogSyncNow.FromApi(), ClogSyncNow)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation");

        app.MapGet(AppRoutes.AdminSourceSearch.FromApi(), SearchSources)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        // Read-only impact preview shown before a rename/merge is committed.
        app.MapGet(AppRoutes.AdminSourceRenamePreview.FromApi(), PreviewRename)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        // Re-renders a single source row (used to cancel out of the preview).
        app.MapGet(AppRoutes.AdminSourceRow.FromApi(), GetSourceRow)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("read");

        // Rename takes the new name from a form field (dynamic), so antiforgery is disabled;
        // it's admin-gated, rate-limited, and the auth cookie is SameSite=Strict.
        return app.MapPost(AppRoutes.AdminSourceRename.FromApi(), RenameSource)
            .RequireAuthorization(nameof(RoleName.Admin))
            .RequireRateLimiting("mutation")
            .DisableAntiforgery();
    }

    private static RazorComponentResult GetHub()
        => IResultExtensions.Component<AdminSettingsHub>();

    // Unknown slugs resolve to the default section inside the component rather than 404ing — see
    // AdminSections.Resolve.
    private static RazorComponentResult GetHubSection(string section)
        => IResultExtensions.Component<AdminSettingsHub>(new { Section = section });

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

    private static async Task<RazorComponentResult> GetRecordAudit(RecordAuditHandler handler)
    {
        var characters = await handler.GetCharacters();
        return IResultExtensions.Component<RecordAuditPanel>(new { Characters = characters });
    }

    /// The source list depends on the chosen character, so it is its own fetch rather than being
    /// shipped up-front for every character at once.
    private static async Task<RazorComponentResult> GetRecordAuditSources(int characterId, RecordAuditHandler handler)
    {
        var sources = await handler.GetSources(characterId);
        return IResultExtensions.Component<RecordAuditSourceOptions>(new { Sources = sources });
    }

    private static async Task<RazorComponentResult> SearchRecordAudit(
        int characterId, string? sourceName, string? term, int? page, int? pageSize, RecordAuditHandler handler)
    {
        var result = await handler.Search(characterId, sourceName, term, page ?? 1, pageSize ?? RecordAuditHandler.DefaultPageSize);
        return IResultExtensions.Component<RecordAuditResults>(new
        {
            Page = result, CharacterId = characterId, SourceName = sourceName ?? "", Term = term ?? ""
        });
    }

    /// Re-runs the search after deleting so the row disappears and the paging/count stay honest —
    /// removing the row client-side would leave a page of 24 claiming to be a page of 25.
    private static async Task<RazorComponentResult> DeleteAuditRecord(
        int recordId, int characterId, string? sourceName, string? term, int? page, int? pageSize,
        RecordAuditHandler handler)
    {
        await handler.Delete(recordId);
        return await SearchRecordAudit(characterId, sourceName, term, page, pageSize, handler);
    }

    /// Same re-run-the-search shape as the delete: the row has to come back showing its new state,
    /// and the toggle is one button whose label depends on it.
    private static async Task<RazorComponentResult> SetAuditRecordLuckExclusion(
        int recordId, bool excluded, int characterId, string? sourceName, string? term, int? page, int? pageSize,
        RecordAuditHandler handler)
    {
        await handler.SetLuckExclusion(recordId, excluded);
        return await SearchRecordAudit(characterId, sourceName, term, page, pageSize, handler);
    }

    private static async Task<RazorComponentResult> GetBaselines(CharacterBaselineAdminHandler handler)
    {
        var characters = await handler.GetCharacters();
        var rows = await handler.List();
        return IResultExtensions.Component<CharacterBaselinePanel>(new { Characters = characters, Rows = rows });
    }

    private static async Task<RazorComponentResult> GetDelveDepths(CharacterDelveDepthAdminHandler handler)
    {
        var characters = await handler.GetCharacters();
        var rows = await handler.List();
        return IResultExtensions.Component<DelveDepthPanel>(new { Characters = characters, Rows = rows });
    }

    private static async Task<RazorComponentResult> SetDelveDepth(
        [FromForm] int characterId,
        [FromForm] string sourceName,
        [FromForm] int averageDepth,
        CharacterDelveDepthAdminHandler handler)
    {
        var rows = await handler.Set(characterId, sourceName, averageDepth);
        return IResultExtensions.Component<DelveDepthResults>(new { Rows = rows });
    }

    private static async Task<RazorComponentResult> RemoveDelveDepth(
        [FromQuery] int characterId,
        [FromQuery] string source,
        CharacterDelveDepthAdminHandler handler)
    {
        var rows = await handler.Remove(characterId, source);
        return IResultExtensions.Component<DelveDepthResults>(new { Rows = rows });
    }

    private static async Task<RazorComponentResult> SetBaseline(
        [FromForm] int characterId,
        [FromForm] string sourceName,
        [FromForm] int baselineKc,
        CharacterBaselineAdminHandler handler)
    {
        var rows = await handler.Set(characterId, sourceName, baselineKc);
        return IResultExtensions.Component<CharacterBaselineResults>(new { Rows = rows });
    }

    private static async Task<RazorComponentResult> GetItemValues(ItemValueOverrideAdminHandler handler)
    {
        var rows = await handler.List();
        return IResultExtensions.Component<ItemValuePanel>(new { Rows = rows });
    }

    private static async Task<RazorComponentResult> SearchItemValues(
        [FromQuery] string? searchTerm,
        ItemValueOverrideAdminHandler handler)
    {
        var items = await handler.SearchItems(searchTerm);
        return IResultExtensions.Component<ItemValueCandidates>(new { Items = items, SearchTerm = searchTerm });
    }

    private static async Task<RazorComponentResult> GetZeroValueItems(ItemValueOverrideAdminHandler handler)
    {
        var items = await handler.FindZeroValueItems();
        return IResultExtensions.Component<ItemValueZeroReport>(new { Items = items });
    }

    private static async Task<RazorComponentResult> SetItemValue(
        [FromForm] int itemId,
        [FromForm] string itemName,
        [FromForm] int value,
        ItemValueOverrideAdminHandler handler)
    {
        var result = await handler.Set(itemId, itemName, value);
        return IResultExtensions.Component<ItemValueResults>(new
        {
            Rows = result.IsSuccess ? result.Value : [],
            ErrorMessage = result.IsSuccess ? null : result.ErrorMessage
        });
    }

    private static async Task<RazorComponentResult> RemoveItemValue(
        [FromQuery] int itemId,
        ItemValueOverrideAdminHandler handler)
    {
        var rows = await handler.Remove(itemId);
        return IResultExtensions.Component<ItemValueResults>(new { Rows = rows });
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

    private static async Task<RazorComponentResult> RunJobNow(
        [FromQuery] string jobName,
        JobHealthHandler handler)
    {
        await handler.RequestManualRun(jobName);

        // Re-render the expanded view so the button flips to the "requested" state; fall back to
        // the grid if the job somehow has no recorded run yet.
        var rows = await handler.GetHealth();
        var row = rows.FirstOrDefault(r => r.JobName == jobName);
        if (row is null)
            return IResultExtensions.Component<JobHealthPanel>(new { Rows = rows });

        var runs = await handler.GetHistory(jobName);
        return IResultExtensions.Component<JobHealthExpanded>(new { Row = row, Runs = runs });
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
