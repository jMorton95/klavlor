namespace KlavLor.Domain.Entities;

/// <summary>
/// One collection-log entry reduced to what classification needs: its item id and its name.
/// Both are required because they don't always agree — several items reach us under an id the
/// synced log doesn't carry, so an id-only check misclassifies them as ordinary loot.
/// </summary>
public readonly record struct CollectionLogEntryRef(int ItemId, string Name);
