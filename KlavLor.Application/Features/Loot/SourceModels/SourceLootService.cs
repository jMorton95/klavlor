using System.Text.Json;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.SourceModels;

// Consumer-facing facade over the source loot strategies. Callers pass a source name plus raw
// claim data and get computed results back; the keyed strategy selection is fully hidden here,
// so no consumer ever branches on source name. New edge-case sources are added by registering
// another ISourceLootStrategy — nothing here or in the consumers changes.
public sealed class SourceLootService
{
    // Bump when any strategy's derivation logic changes (e.g. Doom's rate table or depth
    // estimator is revised). The backfill service then re-derives every special-source record
    // whose stored EffectiveKillsVersion is below this. Never lower it.
    public const int DerivationVersion = 2;

    private readonly IReadOnlyDictionary<string, ISourceLootStrategy> _special;
    private readonly ISourceLootStrategy _default;
    private readonly ISourceRateModifierCache _modifiers;

    public SourceLootService(IEnumerable<ISourceLootStrategy> strategies, ISourceRateModifierCache modifiers)
    {
        _modifiers = modifiers;
        var all = strategies.ToList();
        _default = all.First(s => string.IsNullOrEmpty(s.SourceName));
        _special = all
            .Where(s => !string.IsNullOrEmpty(s.SourceName))
            .ToDictionary(s => s.SourceName, s => s, StringComparer.OrdinalIgnoreCase);
    }

    // Source names that have a dedicated strategy — the only sources the derivation backfill
    // needs to consider (everything else is left untouched at one roll per kill).
    public IReadOnlyCollection<string> SpecialSourceNames => _special.Keys.ToList();

    // True when the source has a dedicated (non-default) strategy — used to keep such sources
    // off consumers (like the flat-rate leaderboard) that can't yet interpret their maths.
    public bool HasSpecialModel(string sourceName) => _special.ContainsKey(sourceName);

    // Effective kill-count / roll-through contribution of one claim. 1 for ordinary sources;
    // for Doom of Mokhaiotl, the run's estimated delve depth.
    public int EffectiveKills(string sourceName, IReadOnlyList<ClaimDrop> drops) =>
        Resolve(sourceName).EffectiveKills(drops);

    // Whether the source should appear on the luck leaderboard at all.
    public bool IncludeInLeaderboard(string sourceName) => Resolve(sourceName).IncludeInLeaderboard;

    // THE single source of truth for "how many kills should this drop have taken". Normalises per
    // the source's model (flat rate for ordinary sources, unique-table-share scaling for raids,
    // per-run depth for Doom) and then applies the admin rate modifier, which is a GLOBAL
    // baseline: every luck figure, rate column, leaderboard row and feed card must come through
    // here so an admin override is reflected identically everywhere. Never recompute
    // numerator/denominator/rolls by hand at a call site.
    //
    // runDepths carries the depth of every actual run at this source, for depth-modelled sources
    // only; pass null/empty for ordinary sources. itemName may be null/empty for a source-wide value.
    public double ExpectedCompletions(
        string sourceName, string? itemName, int numerator, int denominator, int rolls,
        IReadOnlyList<int>? runDepths = null)
    {
        var strategy = Resolve(sourceName);
        // Depth-modelled sources (Doom) derive per-item odds from the real per-run depths and
        // ignore the flat rate entirely; the admin modifier still applies on top.
        if (runDepths is { Count: > 0 } && itemName is not null
            && strategy.ExpectedCompletionsForRuns(itemName, runDepths) is { } delve && delve > 0)
            return delve * _modifiers.GetMultiplier(sourceName, itemName);

        // A strategy that owns its source's rates must not fall back to the stored wiki value for
        // items it doesn't model — that value isn't a per-run chance and produces nonsense luck.
        if (strategy.OverridesStoredRates)
            return double.MaxValue;

        var baseline = strategy.ExpectedCompletions(numerator, denominator, rolls);
        return baseline * _modifiers.GetMultiplier(sourceName, itemName);
    }

    // Effective expected KC plus its display form ("1/540"), or null when there is no usable
    // rate. Use this anywhere a rate is shown to a user: it already includes the source model and
    // the admin modifier, so it can differ from the raw stored Rarity — and it is populated for
    // depth-modelled sources that have no stored Rarity at all.
    public (double ExpectedKc, string Rarity)? EffectiveRate(
        string sourceName, string? itemName, int? numerator, int? denominator, int rolls,
        IReadOnlyList<int>? runDepths = null)
    {
        var expected = ExpectedCompletions(sourceName, itemName, numerator ?? 1, denominator ?? 0, rolls, runDepths);
        if (expected is <= 0 or >= double.MaxValue || double.IsNaN(expected)) return null;
        // Name the unit for depth-modelled sources. Doom's figure is expected DELVES, and it can sit
        // well outside the wiki's per-level band (1/1,350 down to 1/540 for Avernic treads) because
        // the average is taken over every delve including the shallow levels where the item cannot
        // roll at all. A bare "1/2,269" printed beside a wiki rate of 1/1,350 reads as a bug; saying
        // "delves" makes it a different, correct statistic rather than a mysterious one.
        var label = HasDepthModel(sourceName)
            ? $"1/{Math.Round(expected):N0} delves"
            : $"1/{Math.Round(expected):N0}";
        return (expected, label);
    }

    // Turns the raw run list the repository returns into the depth profile the luck maths needs.
    // Every consumer of SourceCollection.Runs must go through this, because the repository cannot
    // know which sources model depth:
    //
    //   - sources with no depth model get an EMPTY list (raids store an EffectiveKills of 1 per
    //     completion, which is a roll count, not a depth — treating it as one had Chambers of Xeric
    //     reporting "520 delves across 520 runs"), and
    //   - depth-modelled sources get any missing depth derived from the claim's own drops, so the
    //     model works on records the backfill hasn't stamped yet instead of silently degrading to a
    //     plain run count. That gate is exactly why Doom looked broken.
    public IReadOnlyList<SourceRun> NormaliseRuns(string sourceName, IReadOnlyList<SourceRun> runs)
    {
        if (!HasDepthModel(sourceName) || runs.Count == 0) return [];

        var result = new List<SourceRun>(runs.Count);
        foreach (var run in runs)
        {
            result.Add(run.Depth > 0
                ? run
                : run with { Depth = EffectiveKills(sourceName, ParseClaim(run.DropsJson)) });
        }
        return result;
    }

    private static List<ClaimDrop> ParseClaim(string? dropsJson)
    {
        if (string.IsNullOrWhiteSpace(dropsJson)) return [];
        try
        {
            var drops = JsonSerializer.Deserialize<List<LootDrop>>(dropsJson);
            return drops is null ? [] : drops.Select(d => new ClaimDrop(d.Name, d.Quantity)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // True when this source's strategy owns its rates outright, so callers know that "no effective
    // rate" means "we genuinely have none" rather than "fall back to the stored wiki value".
    public bool OverridesStoredRates(string sourceName) => Resolve(sourceName).OverridesStoredRates;

    // True when this source's stored EffectiveKills is a delve DEPTH, so luck should be judged in
    // delves. False for raids, whose EffectiveKills is always 1 (one completion) and which must not
    // be presented as depth-modelled.
    public bool HasDepthModel(string sourceName) => Resolve(sourceName).HasDepthModel;

    private ISourceLootStrategy Resolve(string sourceName) =>
        _special.TryGetValue(sourceName, out var strategy) ? strategy : _default;
}
