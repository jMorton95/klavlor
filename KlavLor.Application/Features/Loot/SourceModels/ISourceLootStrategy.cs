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

    // Whether this source appears on the luck leaderboard. False for sources whose luck the
    // board can't meaningfully compute from a flat rate (e.g. Doom's delve model).
    bool IncludeInLeaderboard { get; }

    // Expected source completions to a first drop of an item, for one player, given the item's
    // stored wiki rate. Default treats numerator/denominator as a flat per-kill probability.
    // Raid strategies reinterpret unique-table shares and scale by the per-completion unique
    // frequency, since the wiki stores a share ("given a unique, which item") not a per-raid rate.
    double ExpectedCompletions(int numerator, int denominator, int rolls);

    // Depth-aware expected completions (in runs) to a first drop of a named item, for sources
    // whose per-item odds depend on how far the run went (Doom's delve levels). Returns null for
    // sources with no depth model, so the caller falls back to the flat ExpectedCompletions.
    double? ExpectedCompletionsForDepth(string itemName, int depth) => null;
}

public abstract class SourceLootStrategy(string sourceName) : ISourceLootStrategy
{
    public string SourceName { get; } = sourceName;

    public abstract int EffectiveKills(IReadOnlyList<ClaimDrop> drops);

    public virtual bool IncludeInLeaderboard => true;

    public virtual double ExpectedCompletions(int numerator, int denominator, int rolls)
    {
        if (denominator <= 0) return double.MaxValue;
        var p = Math.Max(1, rolls) * (double)Math.Max(1, numerator) / denominator;
        return p <= 0 ? double.MaxValue : 1.0 / p;
    }
}
