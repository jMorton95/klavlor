using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Feed;

public sealed class LootFeedTiersHandler(
    ILootLogRepository lootLogRepository,
    IDropRateRepository dropRateRepository,
    SourceLootService sourceLoot)
{
    public const int EntriesPerTier = 50;

    public async Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> Handle(
        LootFeedScope scope = LootFeedScope.Main,
        IReadOnlySet<LootFeedTier>? requestedTiers = null)
    {
        var tiers = await lootLogRepository.GetAllFeedTiers(EntriesPerTier, scope, requestedTiers);
        return await AttachEffectiveRates(tiers);
    }

    /// <summary>
    /// Stamps every drop on every card with its effective rate, so a card rendered on page load
    /// carries the same luck figures as one pushed live over SSE (which LootIngestHandler stamps at
    /// publish time). Everything routes through SourceLootService, so the source's loot model and
    /// any admin rate modifier apply here exactly as on the character page and the leaderboard.
    /// </summary>
    private async Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> AttachEffectiveRates(
        Dictionary<LootFeedTier, List<LootFeedEntry>> tiers)
    {
        // One rate lookup per distinct source across the whole feed, not one per card.
        var itemsBySource = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in tiers.Values.SelectMany(list => list))
        {
            if (!itemsBySource.TryGetValue(entry.SourceName, out var items))
            {
                items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                itemsBySource[entry.SourceName] = items;
            }
            foreach (var drop in entry.Drops) items.Add(drop.Name);
        }

        var ratesBySource = new Dictionary<string, IReadOnlyDictionary<string, DropRate>>(StringComparer.Ordinal);
        foreach (var (source, items) in itemsBySource)
        {
            // Sequential — the scoped DbContext handles one query at a time.
            ratesBySource[source] = await dropRateRepository.GetRates(source, items.ToList());
        }

        foreach (var entries in tiers.Values)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!ratesBySource.TryGetValue(entry.SourceName, out var rates)) continue;

                // This card's own run depth, so a depth-modelled source is rated against the run
                // that produced the drop rather than the character's deepest ever delve.
                var runDepths = entry.RunDepth is > 0 ? new[] { entry.RunDepth.Value } : null;

                entries[i] = entry with
                {
                    Drops = entry.Drops.Select(d =>
                    {
                        rates.TryGetValue(d.Name, out var rate);
                        var effective = sourceLoot.EffectiveRate(
                            entry.SourceName, d.Name, rate?.RarityNumerator, rate?.RarityDenominator,
                            rate?.Rolls ?? 1, runDepths);
                        return d with { ExpectedKc = effective?.ExpectedKc, EffectiveRarity = effective?.Rarity };
                    }).ToList()
                };
            }
        }

        return tiers;
    }
}
