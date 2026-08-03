using KlavLor.Application.Interfaces.Services;

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
        return (expected, $"1/{Math.Round(expected):N0}");
    }

    // True when this source's strategy owns its rates outright, so callers know that "no effective
    // rate" means "we genuinely have none" rather than "fall back to the stored wiki value".
    public bool OverridesStoredRates(string sourceName) => Resolve(sourceName).OverridesStoredRates;

    private ISourceLootStrategy Resolve(string sourceName) =>
        _special.TryGetValue(sourceName, out var strategy) ? strategy : _default;
}
