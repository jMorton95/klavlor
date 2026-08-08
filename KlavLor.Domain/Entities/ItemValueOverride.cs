using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

// Admin-set intrinsic GP value for an item RuneLite reports as worthless. The untradeable
// components of a built item (the Noxious halberd's point/blade/pommel) have no Grand Exchange
// price, so every drop of one arrives priced at 0 and never reaches the feed or any GP total —
// even though the assembled weapon is worth tens of millions.
//
// This is a GLOBAL, timeless override: once set, every receipt of the item by any character, past
// and future, is valued at this figure. It deliberately has no point-in-time semantics — it is not
// a price history and does not try to be. Keyed on ItemId because names collide and get renamed,
// while the item id is stable and is already indexed on LootDrops.
//
// Distinct from LootDrop.IsSpecial, which is the admin-injected zero-value "giga" drop for genuine
// one-offs. A value override changes what an item is WORTH, so it flows through tier classification
// like any other price: set it to 10,000,000 and the drop lands in the Epic swimlane, set it to
// 100,000 and it lands in Uncommon.
public sealed class ItemValueOverride : Entity
{
    [Required]
    public int ItemId { get; set; }

    // Stored for display in the admin list only — never used for matching.
    [Required, StringLength(150)]
    public string ItemName { get; set; } = "";

    // Per-unit GP. Int, matching LootDrop.Price / LootDropRow.Price, so no widening is needed
    // anywhere downstream.
    [Required]
    public int Value { get; set; }
}
