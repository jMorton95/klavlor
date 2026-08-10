using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

/// <summary>
/// Membership of one collection-log item in one category. Many-to-many on purpose: an item can
/// appear under several categories (shared rare-drop-table items, clue rewards spanning tiers), so
/// a single category column on <see cref="CollectionLogItem"/> would lose rows.
/// </summary>
/// <remarks>
/// About 2,500 rows total and effectively static — it only changes when Jagex adds content. Synced
/// reference data, so not an <see cref="Entity"/>.
/// </remarks>
public sealed class CollectionLogCategoryItem
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    public string CategorySlug { get; set; } = "";

    /// <summary>In-game item id — the same key as CollectionLogItem.ItemId and LootDrops.ItemId.</summary>
    public int ItemId { get; set; }

    /// <summary>Position within the category, preserving the order Temple returns.</summary>
    public int SortOrder { get; set; }
}
