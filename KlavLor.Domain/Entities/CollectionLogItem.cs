using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

/// <summary>
/// A wiki-synced reference row marking an OSRS item as part of the collection log.
/// Populated/refreshed from Module:Collection_log/data.json by CollectionLogSyncService;
/// <see cref="ItemId"/> is the natural key (the in-game item id, also stored on each drop).
/// </summary>
public sealed class CollectionLogItem
{
    [Key]
    public int ItemId { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    /// <summary>Collection-log tabs/activities this item belongs to (e.g. "Zulrah", "Slayer"). Stored for future source-scoped classification.</summary>
    public string[]? Tabs { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
