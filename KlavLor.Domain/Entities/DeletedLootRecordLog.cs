using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KlavLor.Domain.Entities;

// A record of a loot record an admin DELETED through the record-audit panel.
//
// Deletion stays irreversible — this is a log, not an archive. It exists because the audit panel's
// other two actions (luck exclusion, drop blacklist) leave a live row you can see and undo, while a
// deletion left no trace at all: months later there was no way to answer "did someone remove this,
// or did it never sync?". The row carries enough to answer that — who, when, whose character, which
// source, when the kill happened, what it was worth and what it contained — and nothing more.
//
// DropsSummary is a flattened, human-readable item list rather than the original DropsJson. Storing
// the JSON would make this an archive by accident, and would quietly keep a copy of data an admin
// has decided to remove. A summary answers the question the log exists for.
//
// Deliberately NOT cleaned up when a character or user is deleted: the point of an audit log is to
// outlive the thing it describes. GameCharacterId is therefore a plain int, not a foreign key.
public sealed class DeletedLootRecordLog : Entity
{
    // The id the record had. Meaningless as a lookup now — kept so a log line can be matched
    // against anything that quoted the old id (a support thread, an earlier screenshot).
    [Required]
    public int LootRecordId { get; set; }

    public int? GameCharacterId { get; set; }

    // Snapshotted rather than joined, so the log still reads correctly after a rename.
    [Required, StringLength(100)]
    public string CharacterName { get; set; } = "";

    [Required, StringLength(100)]
    public string SourceName { get; set; } = "";

    [Required]
    public DateTimeOffset OccurredAt { get; set; }

    public int? KillCount { get; set; }

    [Required]
    public long TotalValue { get; set; }

    // "Torva platelegs, Nihil dust x54" — what the kill carried, for the audit list.
    [Required, StringLength(1000)]
    public string DropsSummary { get; set; } = "";

    [StringLength(500)]
    public string? Reason { get; set; }
}
