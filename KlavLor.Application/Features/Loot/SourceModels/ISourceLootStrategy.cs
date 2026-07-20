namespace KlavLor.Application.Features.Loot.SourceModels;

// One item line from a single claim — the source-agnostic input a strategy reasons over.
public sealed record ClaimDrop(string ItemName, int Quantity);

// Strategy for computing source-specific loot facts, one per edge-case source whose loot
// doesn't follow the flat "one loot-table roll per kill" assumption. Selected by SourceName;
// the empty string marks the default strategy used for every ordinary source. Consumers never
// touch a strategy directly — they call SourceLootService, which dispatches and hands back the
// computed result. Modelled on the PVT strategy convention (marker property + abstract base +
// keyed dictionary dispatch behind a facade), adapted to a string key with a default fallback
// because sources are an open set of names rather than a closed enum.
public interface ISourceLootStrategy
{
    // Dispatch key, matched against LootRecord.SourceName (case-insensitive). Empty = default.
    string SourceName { get; }

    // Effective loot-table roll-throughs / kill-count units one claim represents.
    // Default sources: 1. Doom of Mokhaiotl: the run's estimated delve depth.
    int EffectiveKills(IReadOnlyList<ClaimDrop> drops);
}

public abstract class SourceLootStrategy(string sourceName) : ISourceLootStrategy
{
    public string SourceName { get; } = sourceName;

    public abstract int EffectiveKills(IReadOnlyList<ClaimDrop> drops);
}
