using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// Fetches the per-(source, item) drop rates for one source from the wiki and replaces
/// the stored rows transactionally. Extracted so both the periodic
/// <see cref="DropRateSyncService"/> and the admin "fetch now" feature share one path.
/// </summary>
internal sealed class DropRateSyncRunner(IDropRateRepository repository, IOsrsWikiClient wikiClient)
    : IDropRateSyncRunner
{
    public async Task<DropRateSyncResult> SyncSource(string sourceName, CancellationToken cancellationToken = default)
    {
        var mapping = SourceNameAliases.Resolve(sourceName);
        var wikiRates = await wikiClient.FetchDropRatesForSource(mapping.PageTitle);

        // null == the fetch failed (network / API error). Keep any existing rows untouched and
        // leave the miss state alone so a transient blip during a mass backfill doesn't wrongly
        // flag a good source as having no data — the next cycle retries it.
        if (wikiRates is null)
            return new DropRateSyncResult(sourceName, 0, DropRateSyncOutcome.FetchFailed);

        if (mapping.SectionFilter is not null)
        {
            // Filter to rows whose section heading contains the filter token so a shared
            // wiki page (e.g. The Gauntlet) feeds the right variant rates to each source.
            wikiRates = wikiRates
                .Where(r => r.Section is not null && r.Section.Contains(mapping.SectionFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (wikiRates.Count == 0)
        {
            // Record that this source has nothing on the wiki so it drops out of the
            // admin "missing rates" backlog until explicitly re-checked.
            await repository.MarkNoWikiData(sourceName);
            return new DropRateSyncResult(sourceName, 0, DropRateSyncOutcome.NoData);
        }

        var rates = wikiRates
            // Same item can appear twice (e.g. normal + hard mode); collapse by name keeping
            // the rarer entry so the unique (source, item) index doesn't reject the batch.
            .GroupBy(r => r.ItemName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(EffectiveProbability).First())
            .Select(r => new DropRate
            {
                SourceName = sourceName,
                ItemName = r.ItemName,
                Rarity = r.Rarity,
                RarityNumerator = r.Numerator,
                RarityDenominator = r.Denominator,
                Rolls = r.Rolls,
                Quantity = r.Quantity,
                Notes = r.Section
            })
            .ToList();

        await repository.ReplaceForSource(sourceName, rates);
        await repository.ClearNoWikiData(sourceName); // it has data now — un-hide if previously marked
        return new DropRateSyncResult(sourceName, rates.Count, DropRateSyncOutcome.Synced);
    }

    // Sort key for dedup: rarer drops (lower probability) win. Unparseable denominators
    // sink to the bottom since we can't compare them numerically.
    private static double EffectiveProbability(WikiDropRate r)
    {
        if (r.Denominator is null or 0 || r.Numerator is null) return double.MaxValue;
        return (double)r.Numerator.Value / r.Denominator.Value;
    }
}
