using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

// Singleton cache of the admin drop blacklist. Reads are lock-free against an immutable snapshot;
// Replace swaps the whole map atomically (writes are rare). Keyed on record id, because every read
// already knows which record it is looking at and the per-record set is one or two entries.
internal sealed class DropBlacklistCache : IDropBlacklistCache
{
    // Names are stored lowercased and looked up lowercased, so the tuple's default ordinal string
    // comparison gives case-insensitive matching without a custom comparer.
    private volatile Dictionary<int, HashSet<(int ItemId, string Name)>> _byRecord = [];

    public bool HasAny => _byRecord.Count > 0;

    public bool IsBlacklisted(int lootRecordId, int itemId, string itemName)
    {
        var byRecord = _byRecord;
        return byRecord.Count > 0
               && byRecord.TryGetValue(lootRecordId, out var items)
               && items.Contains((itemId, itemName.ToLowerInvariant()));
    }

    public void Replace(IEnumerable<BlacklistedDropValue> entries)
    {
        var byRecord = new Dictionary<int, HashSet<(int, string)>>();
        foreach (var e in entries)
        {
            if (!byRecord.TryGetValue(e.LootRecordId, out var items))
                byRecord[e.LootRecordId] = items = [];
            items.Add((e.ItemId, e.ItemName.ToLowerInvariant()));
        }

        _byRecord = byRecord;
    }
}
