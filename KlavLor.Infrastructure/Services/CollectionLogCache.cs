using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// Singleton holding an immutable id set swapped atomically on refresh, so reads are lock-free.
/// </summary>
internal sealed class CollectionLogCache : ICollectionLogCache
{
    private volatile IReadOnlySet<int> _itemIds = new HashSet<int>();

    public bool IsCollectionLogItem(int itemId) => _itemIds.Contains(itemId);

    public void Replace(IEnumerable<int> itemIds) => _itemIds = itemIds.ToHashSet();
}
