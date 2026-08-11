using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

public enum CollectionLogSyncOutcome
{
    /// <summary>No sync has been attempted yet.</summary>
    Never,
    /// <summary>Fetched and stored.</summary>
    Ok,
    /// <summary>Temple's last-changed timestamp hadn't moved, so nothing was written.</summary>
    Unchanged,
    /// <summary>Temple knows the player but they have never synced their collection log to it.</summary>
    NotSynced,
    /// <summary>Temple has no such player.</summary>
    NotFound,
    /// <summary>Network or parse failure — see LastError.</summary>
    Failed
}

/// <summary>
/// One row per character summarising its collection log, plus the audit trail of how that summary
/// was arrived at.
/// </summary>
/// <remarks>
/// Denormalised on purpose. Every headline surface — the clan board, a character header, a
/// comparison — needs totals, and computing them from CharacterCollectionLogEntries would mean
/// aggregating thousands of rows per character on every page view. This is one row per character,
/// so the clan board is a single scan of N rows.
///
/// It is also the audit record: which RSN was asked for, what Temple said its own freshness was,
/// when we last succeeded, and why we last failed. ConsecutiveFailures lets the sync back off a
/// character that is permanently broken rather than retrying it hourly forever.
/// </remarks>
public sealed class CharacterCollectionLogState
{
    [Key]
    public int GameCharacterId { get; set; }

    [ForeignKey(nameof(GameCharacterId))]
    public GameCharacter? GameCharacter { get; set; }

    /// <summary>The RSN this state was fetched with — the character's DisplayName at sync time.</summary>
    [Required, StringLength(50)]
    public string Rsn { get; set; } = "";

    /// <summary>Temple's canonical spelling and capitalisation, when it returns one.</summary>
    [StringLength(50)]
    public string? TempleDisplayName { get; set; }

    /// <summary>Temple's game_mode: 0 = main, 1 = ironman, 2 = UIM, 3 = HCIM. Matters for fair comparison.</summary>
    public int GameMode { get; set; }

    public int TotalObtained { get; set; }

    /// <summary>Temple's total_collections_available at sync time — the denominator it used.</summary>
    public int TotalAvailable { get; set; }

    public int CategoriesFinished { get; set; }

    public int CategoriesAvailable { get; set; }

    /// <summary>Official hiscores rank for collections. Null when the player isn't ranked.</summary>
    public int? HiscoresRank { get; set; }

    /// <summary>Temple's last_checked — when the PLAYER last synced to Temple. Drives the staleness warning.</summary>
    public DateTimeOffset? TempleLastChecked { get; set; }

    /// <summary>Temple's last_changed — when their log last gained an item. The skip-the-write signal.</summary>
    public DateTimeOffset? TempleLastChanged { get; set; }

    /// <summary>When we last completed a sync attempt of any outcome.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>When a sync last actually changed our stored entries.</summary>
    public DateTimeOffset? LastChangedAt { get; set; }

    public CollectionLogSyncOutcome LastOutcome { get; set; } = CollectionLogSyncOutcome.Never;

    [StringLength(300)]
    public string? LastError { get; set; }

    /// <summary>Reset to 0 on any success. Used to back off a character that keeps failing.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>True once we hold a usable log — the gate every UI uses before rendering anything.</summary>
    public bool HasData => TotalObtained > 0 && LastOutcome is CollectionLogSyncOutcome.Ok or CollectionLogSyncOutcome.Unchanged;
}
