using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

// Admin decision that ONE item on ONE kill never happened: it is removed from the derived loot
// projection, so it is invisible on every surface of the site — the character's drop grid and loot
// chart, source tables, GP totals, the collection log, the live feed, profile stats, global item
// and source pages. The kill itself is untouched and still counts as a roll, and the record's OTHER
// drops are unaffected.
//
// This is the harder sibling of LootRecord.ExcludedFromLuck. That one disowns the luck ATTRIBUTION
// of a whole record while leaving the drop visible and counted for gold, which is right for a
// receipt that cannot be rated honestly. This one is for a drop that is not loot at all — RuneLite
// logging an item as it is equipped, attributed to whatever dossier or chest was opened at that
// moment. Deleting the record would throw away the real kill to remove one phantom item.
//
// HOW IT HIDES, AND WHY THAT NEEDS NO CHANGES AT THE READ SITES.
// DropsJson stays the canonical, raw, never-rewritten record — that is exactly what makes this
// reversible, since restoring re-derives straight back from it. What the blacklist removes is the
// DERIVED projection: the LootDrops row, and the item's contribution to LootRecords.TotalValue.
// Every SQL read site on the site already reads those, so they all stop seeing the drop without a
// single query changing. The same argument recorded on ItemValueOverride, for the same reason.
//
// The exception is the handful of call sites that deserialise DropsJson directly. Those go through
// EffectiveDropReader, which applies this blacklist and the item-value overrides in one pass, so
// the two representations cannot disagree about what a kill contained.
//
// Matched on item id AND name together. Within one record the id is exactly the id its own
// projection row was built from, so the match is precise; the name is carried because an
// untradeable can be logged with no usable id at all (ItemId 0), and two such drops on one kill
// must remain individually blacklistable.
public sealed class BlacklistedLootDrop : Entity
{
    [Required]
    public int LootRecordId { get; set; }

    [ForeignKey(nameof(LootRecordId))]
    public LootRecord? LootRecord { get; set; }

    [Required]
    public int ItemId { get; set; }

    [Required, StringLength(150)]
    public string ItemName { get; set; } = "";

    // Free-text note from the admin who blacklisted it, shown in the audit list. Optional: the
    // point is to be able to answer "why is this gone" months later without guessing.
    [StringLength(500)]
    public string? Reason { get; set; }
}
