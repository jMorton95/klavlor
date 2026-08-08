using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Services;

// One stored override row, flattened for the cache.
public readonly record struct ItemValueOverrideValue(int ItemId, string ItemName, int Value);

// Singleton in-memory cache of the admin-set intrinsic item values (see ItemValueOverride).
// Reads sit on the hot path — every ingested drop, every feed card, every kill list rendered from
// DropsJson — while writes are a rare admin edit, so it holds an immutable snapshot swapped
// atomically on Replace, exactly like ISourceRateModifierCache.
public interface IItemValueOverrideCache
{
    // The effective per-unit GP for a drop: the admin override if one exists for the item, else the
    // raw price RuneLite reported. This is the ONLY place the two are reconciled.
    int GetPrice(int itemId, int rawPrice);

    // True when at least one override is configured. Lets hot paths skip the rewrite entirely in
    // the overwhelmingly common case where nothing is overridden.
    bool HasAny { get; }

    void Replace(IEnumerable<ItemValueOverrideValue> overrides);
}

public static class ItemValueOverrideCacheExtensions
{
    // Re-prices a drop list read back from DropsJson (which always holds the RAW RuneLite price —
    // see the note on LootDropRow). Every call site that deserialises DropsJson and then looks at a
    // price must go through this, or the same drop reads one way live and another way after the
    // stored projection is queried.
    public static List<LootDrop> WithEffectivePrices(this IItemValueOverrideCache cache, List<LootDrop> drops)
    {
        if (!cache.HasAny || drops.Count == 0) return drops;

        List<LootDrop>? rewritten = null;
        for (var i = 0; i < drops.Count; i++)
        {
            var d = drops[i];
            var price = cache.GetPrice(d.ItemId, d.Price);
            if (price == d.Price) continue;

            rewritten ??= [.. drops];
            rewritten[i] = d with { Price = price };
        }

        return rewritten ?? drops;
    }
}
