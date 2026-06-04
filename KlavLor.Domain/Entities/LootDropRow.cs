using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

// Normalised, indexed projection of one element of a LootRecord's DropsJson array.
// DropsJson remains the canonical, permanent record; these rows are derived from it
// (written alongside on ingest, rebuildable at any time) so item-level queries can use
// real indexes instead of unnesting JSONB. Deliberately does NOT extend Entity — it has
// no independent lifecycle, audit, or concurrency token; it is owned by its LootRecord.
public sealed class LootDropRow
{
    public int Id { get; set; }

    [Required]
    public int LootRecordId { get; set; }

    [ForeignKey(nameof(LootRecordId))]
    public LootRecord? LootRecord { get; set; }

    [Required]
    public int ItemId { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = "";

    [Required]
    public int Quantity { get; set; }

    [Required]
    public int Price { get; set; }

    public bool IsFirstTime { get; set; }
}
