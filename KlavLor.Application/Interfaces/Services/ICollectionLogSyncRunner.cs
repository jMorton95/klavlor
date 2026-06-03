namespace KlavLor.Application.Interfaces.Services;

// Performs one collection-log wiki sync (fetch → replace reference table → refresh cache).
// Shared by the hourly CollectionLogSyncService and the admin "sync now" action.
public interface ICollectionLogSyncRunner
{
    /// <summary>Returns the number of items stored, or 0 if the wiki returned nothing (existing data kept).</summary>
    Task<int> RunOnce(CancellationToken cancellationToken = default);
}
