namespace KlavLor.Application.Interfaces.Services;

// One blacklisted drop, flattened for the cache.
public readonly record struct BlacklistedDropValue(int LootRecordId, int ItemId, string ItemName);

// Singleton in-memory cache of the admin drop blacklist (see BlacklistedLootDrop).
//
// Reads sit on the hot path — every kill list, feed card and session rendered from DropsJson — while
// writes are a rare admin edit, so it holds an immutable snapshot swapped atomically on Replace,
// exactly like IItemValueOverrideCache and ISourceRateModifierCache.
//
// The whole set is held in memory rather than joined per query because it is, by construction, a
// handful of admin decisions in a table of millions: the same reasoning that makes
// LootRecord.ExcludedFromLuck's index a partial one listing only the excluded rows.
public interface IDropBlacklistCache
{
    // True when at least one drop is blacklisted anywhere. Lets hot paths skip the filter entirely
    // in the overwhelmingly common case where nothing is.
    bool HasAny { get; }

    // Matched on id AND name together — see the note on BlacklistedLootDrop for why the name is
    // carried too. Name comparison is case-insensitive: the three vocabularies that produce an item
    // name (the wiki, RuneLite, the collection log) disagree on case.
    bool IsBlacklisted(int lootRecordId, int itemId, string itemName);

    void Replace(IEnumerable<BlacklistedDropValue> entries);
}
