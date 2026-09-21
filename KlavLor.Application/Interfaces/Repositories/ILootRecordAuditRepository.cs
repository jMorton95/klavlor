using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Interfaces.Repositories;

/// <summary>
/// The admin record-audit surface: find one bad sync record and repair it.
///
/// This exists because RuneLite occasionally attributes drops to the wrong source — a dossier
/// opened at the same moment an item was equipped gets logged as loot from the dossier. Those
/// records are individually wrong and there was no way to see or remove one; the only deletion
/// available was "everything for this character", which is not a repair.
///
/// THREE REPAIRS, ANSWERING THREE DIFFERENT QUESTIONS. They are not degrees of the same thing:
///
/// <list type="bullet">
/// <item><description>
/// <b>Exclude from luck</b> — the kill happened and the drop is real, but the receipt cannot be
/// rated honestly. Whole record. The roll still counts, the drop still counts for gold and still
/// shows everywhere; only the luck claim goes.
/// </description></item>
/// <item><description>
/// <b>Blacklist a drop</b> — one item on one kill is not loot at all. The item becomes invisible
/// site-wide and stops counting for gold; the kill still counts as a roll and the record's other
/// drops are untouched. Reversible, because DropsJson still holds it.
/// </description></item>
/// <item><description>
/// <b>Delete</b> — the whole record is wrong. Irreversible, and logged.
/// </description></item>
/// </list>
/// </summary>
public interface ILootRecordAuditRepository
{
    /// <summary>Sources this character has records for, most records first, so the admin picks
    /// from what actually exists rather than typing a name and hoping.</summary>
    Task<List<AuditSourceOption>> GetSources(int gameCharacterId);

    /// <summary>
    /// One page of a character's records. <paramref name="sourceName"/> may be empty when
    /// <paramref name="term"/> is given, which searches that item across every source the character
    /// has — the way you find a mis-attributed drop whose source is the very thing you cannot guess.
    /// </summary>
    Task<AuditRecordPage> Search(int gameCharacterId, string? sourceName, string? term, int page, int pageSize);

    /// <summary>Returns the record's character and source so the caller can invalidate exactly
    /// what the deletion touched, or null when it had already gone. Writes the audit-log row.</summary>
    Task<DeletedRecordInfo?> Delete(int recordId, string? reason);

    /// <summary>
    /// Set or clear the record's luck exclusion. Returns the same information a delete does, so the
    /// caller invalidates exactly what changed, or null when the record has gone.
    /// </summary>
    Task<DeletedRecordInfo?> SetLuckExclusion(int recordId, bool excluded);

    /// <summary>
    /// Blacklist or restore ONE drop on one record. Returns what changed for invalidation, or null
    /// when the record has gone.
    /// </summary>
    /// <remarks>
    /// Persisting the row is only half of it: the derived projection has to be rebuilt for the
    /// record before anything reads it, or the drop stays visible on every SQL surface while the
    /// blacklist claims otherwise. Both happen here so no caller can do one without the other.
    /// </remarks>
    Task<DeletedRecordInfo?> SetDropBlacklist(int recordId, int itemId, string itemName, bool blacklisted, string? reason);

    /// <summary>Every blacklisted drop, for priming <see cref="IDropBlacklistCache"/>.</summary>
    Task<IReadOnlyList<BlacklistedDropValue>> GetAllBlacklistedDrops();

    /// <summary>
    /// Everything an admin has manually done through this panel, newest first: blacklisted drops,
    /// luck-excluded records, and deletions. The panel's answer to "what have I changed".
    /// </summary>
    Task<List<AuditModification>> GetModifications(int limit);
}

public sealed record AuditSourceOption(string SourceName, int RecordCount);

/// <summary>One drop within a record, for display.</summary>
/// <param name="Price">
/// The effective (override-aware) per-unit price, matching what the rest of the site shows.
/// </param>
/// <param name="Blacklisted">
/// True when this drop has been blacklisted. The audit panel is the ONE surface that still shows a
/// blacklisted drop — it reads the canonical DropsJson rather than the projection, precisely so the
/// decision can be seen and lifted. Everywhere else it does not exist.
/// </param>
public sealed record AuditRecordDrop(string Name, int ItemId, int Quantity, long Price, bool Blacklisted);

public sealed record AuditRecordRow(
    int Id,
    string SourceName,
    DateTimeOffset OccurredAt,
    int? KillCount,
    long TotalValue,
    bool IsImported,
    string? ContentHash,
    bool ExcludedFromLuck,
    List<AuditRecordDrop> Drops);

public sealed record AuditRecordPage(
    List<AuditRecordRow> Rows,
    int Page,
    int PageSize,
    int TotalRows)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalRows / (double)PageSize) : 0;
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public sealed record DeletedRecordInfo(int GameCharacterId, string SourceName, List<string> ItemNames);

/// <summary>What kind of manual change a row in the modifications list describes.</summary>
public enum AuditModificationKind
{
    /// One drop hidden site-wide. Reversible.
    BlacklistedDrop,

    /// One record's drops taken out of the luck maths. Reversible.
    LuckExcluded,

    /// One record removed. Log only — the row itself is gone.
    Deleted
}

/// <summary>
/// One manual admin change, flattened for the audit list. The three kinds come from three different
/// tables and are unioned in memory rather than in SQL: they share almost no columns, and the list
/// is bounded to the most recent handful, so a union query would be more machinery for less clarity.
/// </summary>
/// <param name="RecordId">
/// The affected record. For a deletion it is the id the record HAD — useful for matching against
/// anything that quoted it, not for looking anything up.
/// </param>
/// <param name="Restorable">
/// Whether the panel can offer an undo. False for a deletion, which is log-only.
/// </param>
public sealed record AuditModification(
    AuditModificationKind Kind,
    int RecordId,
    int? GameCharacterId,
    string CharacterName,
    string SourceName,
    DateTimeOffset OccurredAt,
    int? KillCount,
    long TotalValue,
    string Detail,
    int ItemId,
    string? Reason,
    string? ChangedBy,
    DateTimeOffset ChangedAt,
    bool Restorable);
