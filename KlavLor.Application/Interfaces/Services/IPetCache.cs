using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Services;

// Singleton in-memory set of every pet in the game, taken from Temple's all_pets collection-log
// category. Read on the feed's hot path - every live publish and every backfilled card - to put pets
// in the Legendary lane: RuneLite prices a pet at 0 GP, so by value alone it never clears the 10k
// floor.
//
// Refreshed only when the list can change: primed at startup, then replaced after each taxonomy pull
// (CollectionLogSyncFromTempleService, at most once a day). Holds an immutable snapshot swapped
// atomically on Replace, like IItemValueOverrideCache.
public interface IPetCache
{
    // Id OR name, case-insensitive - the same rule as ICollectionLogCache.IsCollectionLogItem, for the
    // same reason: the id RuneLite reports is not always the id the taxonomy carries.
    bool IsPet(int itemId, string? itemName = null);

    // The raw sets, for the backfill query that finds records carrying a pet. They go to SQL as
    // parameters: resolving the list inside the query instead made the planner misestimate the match
    // count by ~400x and sequentially scan LootRecords.
    IReadOnlyCollection<int> ItemIds { get; }
    IReadOnlyCollection<string> LowerNames { get; }

    void Replace(IEnumerable<CollectionLogEntryRef> pets);
}
