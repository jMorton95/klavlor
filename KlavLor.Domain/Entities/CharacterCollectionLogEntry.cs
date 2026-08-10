namespace KlavLor.Domain.Entities;

/// <summary>
/// One collection-log item a character owns, as reported by TempleOSRS. Composite key of
/// (GameCharacterId, ItemId).
/// </summary>
/// <remarks>
/// CURRENT STATE ONLY — never a history. The table is deliberately bounded at
/// characters × 1,712 items (~100KB per fully-complete character), so it grows with people and
/// their progress, not with time. Snapshotting this hourly instead would add 1,712 × 24 × 365 rows
/// per character per year, which is the one design mistake that would make this unmanageable.
/// Syncs update in place; the audit trail lives on <see cref="CharacterCollectionLogState"/>.
///
/// Not an <see cref="Entity"/>: it is derived from an upstream, so an audit stamp and a concurrency
/// token would be churn on every sync for no benefit.
/// </remarks>
public sealed class CharacterCollectionLogEntry
{
    public int GameCharacterId { get; set; }

    /// <summary>In-game item id. Joins to CollectionLogItem, LootDrops and ItemValueOverride alike.</summary>
    public int ItemId { get; set; }

    /// <summary>How many the character has logged. Temple reports 0 for owned-but-uncounted items.</summary>
    public int Count { get; set; }

    /// <summary>
    /// When Temple says it was obtained. Null when Temple holds no date — common for items obtained
    /// before the player started syncing. A null here is why a UI must not assume a date exists.
    /// </summary>
    public DateTimeOffset? ObtainedAt { get; set; }

    /// <summary>When OUR sync first saw this entry — distinct from ObtainedAt, and never rewritten.</summary>
    public DateTimeOffset FirstSeenAt { get; set; }

    /// <summary>When the last sync confirmed it. Lets a stale row be spotted after an upstream removal.</summary>
    public DateTimeOffset LastSyncedAt { get; set; }
}
