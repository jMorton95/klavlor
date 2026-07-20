using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public sealed class LootRecord : Entity
{
    [Required]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required, StringLength(100)]
    public string SourceName { get; set; } = "";

    [Required]
    public LootSourceType SourceType { get; set; }

    public int? CombatLevel { get; set; }

    public int? KillCount { get; set; }

    [Required]
    public long TotalValue { get; set; }

    [Required, Column(TypeName = "jsonb")]
    public string DropsJson { get; set; } = "[]";

    [Required]
    public DateTimeOffset OccurredAt { get; set; }

    [StringLength(64)]
    public string? ContentHash { get; set; }

    public bool IsImported { get; set; }

    public int? GameCharacterId { get; set; }

    [ForeignKey(nameof(GameCharacterId))]
    public GameCharacter? GameCharacter { get; set; }

    // Generic per-source derived metric computed by a source loot strategy at ingest and
    // re-runnable later. Null for ordinary sources (implicitly one roll per kill); for edge
    // cases it holds the effective roll/kill weight — for Doom of Mokhaiotl, the run's
    // estimated delve depth, so a multi-delve claim can count as more than one kill without
    // splitting the single loot record. Interpreted through SourceLootService, never inline.
    public int? EffectiveKills { get; set; }

    // Derivation marker: the SourceLootService.DerivationVersion under which EffectiveKills
    // was last computed. Null = never derived (a special-source record ingested before the
    // strategy existed, i.e. a production backlog row). The backfill service selects rows
    // where this is null or below the current version, so it (a) cheaply knows whether any
    // work is outstanding and (b) re-derives everything when the strategy logic is bumped —
    // ordinary-source rows are never touched, so the bulk of the table is left untouched.
    public int? EffectiveKillsVersion { get; set; }

    // Normalised projection of DropsJson (see LootDropRow). DropsJson stays canonical;
    // these rows are kept in lock-step on write and are fully rebuildable from it.
    private readonly List<LootDropRow> _drops = [];
    public IReadOnlyCollection<LootDropRow> Drops => _drops.AsReadOnly();

    // Replaces the projected drop rows from the canonical drop list (called on ingest
    // after the in-memory drops are finalised, so both representations agree).
    public void ReplaceDropRows(IEnumerable<LootDropRow> rows)
    {
        _drops.Clear();
        _drops.AddRange(rows);
    }
}
