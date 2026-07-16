using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.DropRates;

// Backs the admin "drop rates" panel: lists sources missing rates (or matching a search)
// and runs an on-demand wiki fetch for a chosen source.
public sealed class DropRateAdminHandler(IDropRateRepository repository, IDropRateSyncRunner runner)
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
        var result = await runner.SyncSource(sourceName);
        var note = result.Outcome switch
        {
            DropRateSyncOutcome.Synced => $"Stored {result.RatesStored} rate{(result.RatesStored == 1 ? "" : "s")}",
            DropRateSyncOutcome.NoData => "No drop-rate data found on the wiki",
            _ => "Could not reach the wiki — existing rates kept, will retry"
        };
        return new DropRateSourceRow(sourceName, result.RatesStored, note);
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
