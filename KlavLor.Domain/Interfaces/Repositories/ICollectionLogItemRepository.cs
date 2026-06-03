using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface ICollectionLogItemRepository
{
    /// <summary>Replaces the entire collection-log reference set in one transaction. No-op when <paramref name="items"/> is empty (never clobbers existing data).</summary>
    Task ReplaceAll(IReadOnlyCollection<CollectionLogItem> items);

    /// <summary>All known collection-log item ids — used to prime the in-memory cache on startup.</summary>
    Task<IReadOnlyList<int>> GetAllItemIds();

    /// <summary>(item count, most recent SyncedAt) for the admin sync-health panel.</summary>
    Task<(int Count, DateTimeOffset? LastSynced)> GetStatus();
}
