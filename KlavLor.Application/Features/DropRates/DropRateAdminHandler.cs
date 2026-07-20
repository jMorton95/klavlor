using Microsoft.Extensions.Logging;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.DropRates;

// Backs the admin "drop rates" panel: lists sources missing rates (or matching a search)
// and runs an on-demand wiki fetch for a chosen source.
public sealed class DropRateAdminHandler(
    IDropRateRepository repository,
    IDropRateSyncRunner runner,
    ILogger<DropRateAdminHandler> logger)
{
    public const int SearchLimit = 40;

    public async Task<List<DropRateSourceRow>> Search(string? term, bool showNoData)
    {
        var known = await repository.GetKnownSourceNames();
        var counts = await repository.GetRateCountsBySource();
        var noData = (await repository.GetNoWikiDataSources()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> sources;
        if (!string.IsNullOrWhiteSpace(term))
        {
            // Explicit search shows everything matching, including no-data sources.
            var t = term.Trim();
            sources = known.Where(s => s.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        else if (showNoData)
        {
            // The "previously found no wiki data" view, for re-checking.
            sources = known.Where(s => noData.Contains(s));
        }
        else
        {
            // Default backlog: loot but no stored rates, and not already known-empty.
            sources = known.Where(s => (!counts.TryGetValue(s, out var c) || c == 0) && !noData.Contains(s));
        }

        return sources
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Take(SearchLimit)
            .Select(s => new DropRateSourceRow(
                s,
                counts.TryGetValue(s, out var c) ? c : 0,
                noData.Contains(s) ? "no wiki data" : null))
            .ToList();
    }

    public async Task<DropRateSourceRow> Sync(string sourceName)
    {
        var (row, _) = await SyncWithOutcome(sourceName);
        return row;
    }

    // Like Sync, but also returns the raw outcome so the bulk resync can tally
    // stored / no-data / failed counts as it walks the backlog.
    //
    // Any exception from the sync (e.g. a RepositoryException when a source's wiki data
    // trips a DB constraint) is swallowed and reported as a failed row rather than thrown:
    // one bad source must never 500 the request and abort the whole bulk resync chain.
    // The underlying exception is already logged at Error level by the repository; we log
    // here too so the failing source name is captured.
    public async Task<(DropRateSourceRow Row, DropRateSyncOutcome Outcome)> SyncWithOutcome(string sourceName)
    {
        try
        {
            var result = await runner.SyncSource(sourceName);
            var note = result.Outcome switch
            {
                DropRateSyncOutcome.Synced => $"Stored {result.RatesStored} rate{(result.RatesStored == 1 ? "" : "s")}",
                DropRateSyncOutcome.NoData => "No drop-rate data found on the wiki",
                _ => "Could not reach the wiki — existing rates kept, will retry"
            };
            return (new DropRateSourceRow(sourceName, result.RatesStored, note), result.Outcome);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Drop-rate sync threw for source {Source}", sourceName);
            return (new DropRateSourceRow(sourceName, 0, "Sync failed unexpectedly — skipped"), DropRateSyncOutcome.FetchFailed);
        }
    }

    // Every source that has loot but no stored drop rates, ordered so the bulk
    // resync walks them alphabetically. When includeNoData is true the sources we
    // previously flagged as having no wiki data are re-checked as well (they are the
    // ones a fresh fetch is most likely to now rescue).
    public async Task<List<string>> GetResyncBacklog(bool includeNoData)
    {
        var known = await repository.GetKnownSourceNames();
        var counts = await repository.GetRateCountsBySource();
        var noData = (await repository.GetNoWikiDataSources()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return known
            .Where(s => (!counts.TryGetValue(s, out var c) || c == 0) && (includeNoData || !noData.Contains(s)))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public const int MissingLimit = 100;

    public async Task<(List<ClogMissingRate> Items, int Total)> GetMissingRates()
    {
        var items = await repository.GetClogItemsMissingRates(MissingLimit);
        var total = await repository.CountClogItemsMissingRates();

        var rows = items
            .Select(c => new ClogMissingRate(
                c.Name,
                c.Tabs is { Length: > 0 } ? string.Join(", ", c.Tabs) : null))
            .ToList();

        return (rows, total);
    }
}
