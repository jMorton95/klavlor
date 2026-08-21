namespace KlavLor.Application.Interfaces.Repositories;

/// <summary>
/// The admin record-audit surface: find one bad sync record and either remove it or take it out of
/// the luck maths.
///
/// This exists because RuneLite occasionally attributes drops to the wrong source — a dossier
/// opened at the same moment an item was equipped gets logged as loot from the dossier. Those
/// records are individually wrong and there was no way to see or remove one; the only deletion
/// available was "everything for this character", which is not a repair.
///
/// Deletion and exclusion answer two different questions. Delete a record that never happened.
/// Exclude one where the KILL happened but the drop cannot be rated honestly — a crystal armour
/// seed logged against Hunllef — so the roll still counts and only the luck claim goes.
/// </summary>
public interface ILootRecordAuditRepository
{
    /// <summary>Sources this character has records for, most records first, so the admin picks
    /// from what actually exists rather than typing a name and hoping.</summary>
    Task<List<AuditSourceOption>> GetSources(int gameCharacterId);

    Task<AuditRecordPage> Search(int gameCharacterId, string sourceName, string? term, int page, int pageSize);

    /// <summary>Returns the record's character and source so the caller can invalidate exactly
    /// what the deletion touched, or null when it had already gone.</summary>
    Task<DeletedRecordInfo?> Delete(int recordId);

    /// <summary>
    /// Set or clear the record's luck exclusion. Returns the same information a delete does, so the
    /// caller invalidates exactly what changed, or null when the record has gone.
    /// </summary>
    Task<DeletedRecordInfo?> SetLuckExclusion(int recordId, bool excluded);
}

public sealed record AuditSourceOption(string SourceName, int RecordCount);

/// <summary>One drop within a record, for display. Price is the effective (override-aware) one
/// already stored on the projection.</summary>
public sealed record AuditRecordDrop(string Name, int Quantity, long Price);

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
