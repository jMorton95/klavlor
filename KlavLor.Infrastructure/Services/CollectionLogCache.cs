using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// Singleton holding immutable lookups swapped atomically on refresh, so reads are lock-free.
/// Holds names as well as ids — see ICollectionLogCache.IsCollectionLogItem for why both are needed.
/// </summary>
internal sealed class CollectionLogCache : ICollectionLogCache
{
    private volatile IReadOnlySet<int> _itemIds = new HashSet<int>();
    private volatile IReadOnlySet<string> _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsCollectionLogItem(int itemId, string? itemName = null)
    {
        if (_itemIds.Contains(itemId)) return true;
        return !string.IsNullOrEmpty(itemName) && _names.Contains(itemName);
    }

    public void Replace(IEnumerable<CollectionLogEntryRef> entries)
    {
        var list = entries as IReadOnlyCollection<CollectionLogEntryRef> ?? entries.ToList();
        _itemIds = list.Select(e => e.ItemId).ToHashSet();
        _names = list
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
