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
    public const int DerivationVersion = 1;

    private readonly IReadOnlyDictionary<string, ISourceLootStrategy> _special;
    private readonly ISourceLootStrategy _default;

    public SourceLootService(IEnumerable<ISourceLootStrategy> strategies)
    {
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

    private ISourceLootStrategy Resolve(string sourceName) =>
        _special.TryGetValue(sourceName, out var strategy) ? strategy : _default;
}
