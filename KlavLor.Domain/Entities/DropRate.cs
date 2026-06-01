using System.ComponentModel.DataAnnotations;

namespace KlavLor.Domain.Entities;

/// <summary>
/// A wiki-sourced drop rate for a (<see cref="SourceName"/>, <see cref="ItemName"/>) pair.
/// Populated by DropRateSyncService from {{DropsLine}} templates on the source's wiki page.
/// SourceName matches LootRecord.SourceName (post-alias). ItemName is verbatim from the wiki
/// and is joined against CollectionLogItem.Name (case-insensitive). Rate fields are nullable
/// because complex wiki rarities (e.g. {{Brimstone rarity|725}}) aren't reduced to N/D form.
/// </summary>
public sealed class DropRate
{
    [Required, Key]
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string SourceName { get; set; } = "";

    [Required, StringLength(200)]
    public string ItemName { get; set; } = "";

    /// <summary>Resolved opportunistically from CollectionLogItem on write; null when the wiki item isn't in our clog reference set.</summary>
    public int? ItemId { get; set; }

    /// <summary>Raw rarity string from the wiki, kept verbatim for debugging and fallback display.</summary>
    [Required, StringLength(120)]
    public string Rarity { get; set; } = "";

    /// <summary>Numerator from parsed "N/D" rarity, null when rarity isn't plain numeric.</summary>
    public int? RarityNumerator { get; set; }

    /// <summary>Denominator from parsed "N/D" rarity, null when rarity isn't plain numeric.</summary>
    public int? RarityDenominator { get; set; }

    /// <summary>"rolls=" parameter on the DropsLine — extra chances per kill. Defaults to 1.</summary>
    public int Rolls { get; set; } = 1;

    [StringLength(60)]
    public string? Quantity { get; set; }

    /// <summary>Section heading the DropsLine sits under (e.g. "Tertiary", "Hard mode"). Surfaces variant context.</summary>
    [StringLength(80)]
    public string? Notes { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
