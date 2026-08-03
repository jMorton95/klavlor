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
    // whose per-item odds depend on how far each run went (Doom's delve levels).
    //
    // Takes the depth of EVERY actual run, never a single summary depth: a max-ever depth would
    // assume every run reached the deepest level the player has ever hit, overstating the odds
    // and making everyone look drier than they are. Returns null for sources with no depth
    // model, so the caller falls back to the flat ExpectedCompletions.
    //
    // Not a default interface method: it must dispatch virtually through the base class so a
    // derived strategy's override is used when called via the interface.
    double? ExpectedCompletionsForRuns(string itemName, IReadOnlyList<int> runDepths);

    // True when this strategy is the sole authority on its source's rates, so an item it does not
    // model has NO usable rate rather than falling back to the stored wiki rate. Needed for
    // depth-modelled sources: Doom's guaranteed accumulating drops (Demon tears, waystones) carry
    // a stored per-level rarity that is meaningless as a per-run chance, and falling back to it
    // reported players as "21x dry" on an item they receive every single run.
    bool OverridesStoredRates { get; }

    // Whether this source's odds depend on how far a run went, i.e. whether EffectiveKills is a
    // DEPTH rather than a roll count. Raids also store an EffectiveKills (always 1, one completion),
    // so "EffectiveKills is set" is NOT the same question and must not be used as a proxy for it —
    // doing so had the character page announcing "790 delves across 790 runs" for Chambers of Xeric.
    bool HasDepthModel { get; }
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

    // No depth model by default; depth-aware sources (Doom) override this.
    public virtual double? ExpectedCompletionsForRuns(string itemName, IReadOnlyList<int> runDepths) => null;

    // Ordinary and raid sources still trust the stored wiki rates.
    public virtual bool OverridesStoredRates => false;

    // Only depth-aware sources (Doom) model depth. Raids also carry an EffectiveKills — always 1,
    // because a raid is one completion — so that field's presence is NOT evidence of a depth model.
    public virtual bool HasDepthModel => false;
}
