using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Services;

/// <summary>
/// In-memory view of the collection log, refreshed periodically from the wiki. Used on the
/// live-feed publish path to classify drops without a DB round-trip.
/// </summary>
public interface ICollectionLogCache
{
    /// <summary>
    /// Whether a drop is a collection-log item. Matches on id OR name, because the two do not always
    /// agree: several items (Avernic treads, Arcane sigil, Dragon thrownaxe, Dragonbone necklace,
    /// Nihil shard) reach us under an id the synced log doesn't carry, and an id-only check silently
    /// classified them as ordinary loot — which is why Avernic treads got no rate on its feed card
    /// while the character page listed it as a collection-log entry. The SQL side already matches
    /// both ways; this brings the in-memory path in line.
    /// </summary>
    bool IsCollectionLogItem(int itemId, string? itemName = null);

    void Replace(IEnumerable<CollectionLogEntryRef> entries);
}
