using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

/// <summary>
/// One collection-log category as TempleOSRS defines it (e.g. "abyssal_sire"), grouped under one of
/// its five top-level groups: bosses, raids, clues, minigames, other.
/// </summary>
/// <remarks>
/// Temple's taxonomy is adopted wholesale rather than the wiki tab strings already on
/// <see cref="CollectionLogItem.Tabs"/>. The two disagree — the wiki carries display names like
/// "Medium Treasure Trails", Temple carries slugs like "medium_treasure_trails" plus the group above
/// them — and every read of a character's log arrives keyed by Temple's slug. Keeping one taxonomy
/// avoids a translation layer that would have to be maintained against two moving upstreams.
///
/// Synced reference data, so deliberately NOT an <see cref="Entity"/>: no audit stamp, no
/// concurrency token, and rebuilt in place by the sync service.
/// </remarks>
public sealed class CollectionLogCategory
{
    [Key, StringLength(80)]
    public string Slug { get; set; } = "";

    /// <summary>Human-readable form derived from the slug ("abyssal_sire" → "Abyssal Sire").</summary>
    [Required, StringLength(120)]
    public string DisplayName { get; set; } = "";

    /// <summary>One of Temple's five groups: bosses, raids, clues, minigames, other.</summary>
    [Required, StringLength(40)]
    public string GroupName { get; set; } = "";

    /// <summary>Items in this category — the denominator for per-category completion.</summary>
    public int ItemCount { get; set; }

    /// <summary>Position within its group, preserving the order Temple returns.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
