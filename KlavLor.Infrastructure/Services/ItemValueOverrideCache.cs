using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

// Singleton cache of the admin-set intrinsic item values. Reads are lock-free against an immutable
// snapshot; Replace swaps the whole map atomically (writes are rare). Keyed on item id.
internal sealed class ItemValueOverrideCache : IItemValueOverrideCache
{
    private volatile Dictionary<int, int> _map = [];

    public bool HasAny => _map.Count > 0;

    public int GetPrice(int itemId, int rawPrice)
    {
        var map = _map;
        return map.Count > 0 && map.TryGetValue(itemId, out var value) ? value : rawPrice;
    }

    public void Replace(IEnumerable<ItemValueOverrideValue> overrides)
    {
        var map = new Dictionary<int, int>();
        foreach (var o in overrides)
            map[o.ItemId] = o.Value;
        _map = map;
    }
}
