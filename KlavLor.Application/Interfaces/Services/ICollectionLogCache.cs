namespace KlavLor.Application.Interfaces.Services;

/// <summary>
/// In-memory set of OSRS collection-log item ids, refreshed periodically from the wiki.
/// Used on the live-feed publish path (in-memory) to classify drops without a DB round-trip.
/// </summary>
public interface ICollectionLogCache
{
    bool IsCollectionLogItem(int itemId);
    void Replace(IEnumerable<int> itemIds);
}
