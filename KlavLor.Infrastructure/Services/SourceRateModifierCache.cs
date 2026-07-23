using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

// Singleton cache of the admin-configured source/item rate multipliers. Reads are lock-free
// against an immutable snapshot; Replace swaps the whole map atomically (writes are rare).
// Keyed by a (source, item) tuple with a case-insensitive comparer — empty item = source-wide.
internal sealed class SourceRateModifierCache : ISourceRateModifierCache
{
    private volatile Dictionary<(string Source, string Item), double> _map = NewMap();

    private static Dictionary<(string Source, string Item), double> NewMap() => new(TupleComparer.Instance);

    public double GetMultiplier(string sourceName, string? itemName)
    {
        var map = _map;
        if (!string.IsNullOrEmpty(itemName) && map.TryGetValue((sourceName, itemName), out var itemMul))
            return itemMul;
        if (map.TryGetValue((sourceName, string.Empty), out var sourceMul))
            return sourceMul;
        return 1.0;
    }

    public void Replace(IEnumerable<SourceRateModifierValue> modifiers)
    {
        var map = NewMap();
        foreach (var m in modifiers)
            map[(m.SourceName, m.ItemName)] = m.Multiplier;
        _map = map;
    }

    private sealed class TupleComparer : IEqualityComparer<(string Source, string Item)>
    {
        public static readonly TupleComparer Instance = new();

        public bool Equals((string Source, string Item) x, (string Source, string Item) y) =>
            string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Item, y.Item, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Source, string Item) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Source),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item));
    }
}
