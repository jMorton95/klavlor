using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.DropRates;

// Backs the admin "drop rates" panel: lists sources missing rates (or matching a search)
// and runs an on-demand wiki fetch for a chosen source.
public sealed class DropRateAdminHandler(IDropRateRepository repository, IDropRateSyncRunner runner)
{
    public const int SearchLimit = 40;

    public async Task<List<DropRateSourceRow>> Search(string? term)
    {
        var known = await repository.GetKnownSourceNames();
        var counts = await repository.GetRateCountsBySource();

        // Blank term → sources with loot but no stored rates (the actionable backlog);
        // otherwise any known source matching the term, so existing rates can be re-fetched.
        IEnumerable<string> sources = string.IsNullOrWhiteSpace(term)
            ? known.Where(s => !counts.TryGetValue(s, out var c) || c == 0)
            : known.Where(s => s.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase));

        return sources
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Take(SearchLimit)
            .Select(s => new DropRateSourceRow(s, counts.TryGetValue(s, out var c) ? c : 0))
            .ToList();
    }

    public async Task<DropRateSourceRow> Sync(string sourceName)
    {
        var result = await runner.SyncSource(sourceName);
        var note = result.FoundWikiData
            ? $"Stored {result.RatesStored} rate{(result.RatesStored == 1 ? "" : "s")}"
            : "No drop-rate data found on the wiki";
        return new DropRateSourceRow(sourceName, result.RatesStored, note);
    }
}
