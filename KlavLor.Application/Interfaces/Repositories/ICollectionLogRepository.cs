using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Interfaces.Repositories;

/// <summary>A character the sync should consider, with the state it last left behind.</summary>
public sealed record CollectionLogSyncTarget(
    int GameCharacterId,
    string Rsn,
    DateTimeOffset? StoredLastChanged,
    DateTimeOffset? LastSyncedAt,
    int ConsecutiveFailures,
    /// <summary>
    /// Entries we actually hold. The skip-if-unchanged shortcut must not fire when this is 0:
    /// a run that stored a last-changed timestamp but no entries (a partial write, or a parse
    /// that yielded nothing) would otherwise be treated as up to date forever.
    /// </summary>
    int StoredEntryCount);

/// <summary>What one character's sync actually did, for the job log.</summary>
public sealed record CollectionLogSyncResult(int Added, int Updated, int Removed);

/// <summary>
/// Write side of the Temple-sourced collection log: the reference taxonomy and each character's
/// entries and state.
/// </summary>
public interface ICollectionLogRepository
{
    /// <summary>
    /// Characters eligible for a sync — visible, with a usable RSN (their DisplayName). Ordered
    /// oldest-synced first so a partial cycle still makes progress across the roster.
    /// </summary>
    Task<List<CollectionLogSyncTarget>> GetSyncTargets();

    /// <summary>Replaces the category taxonomy and its item membership in one transaction.</summary>
    Task ReplaceCategories(IReadOnlyList<TempleCategory> categories);

    /// <summary>True when the taxonomy has never been loaded, so the first cycle knows to fetch it.</summary>
    Task<bool> HasCategories();

    /// <summary>
    /// Applies one character's log: inserts new entries, updates changed ones, deletes entries the
    /// upstream no longer reports, and rewrites the state row. A single transaction, so a character
    /// is never left half-synced.
    /// </summary>
    Task<CollectionLogSyncResult> ApplyPlayerLog(int gameCharacterId, TempleCollectionLog log);

    /// <summary>
    /// Records an attempt that produced no usable log, WITHOUT touching the stored entries — a
    /// player who stopped syncing to Temple keeps the log we already hold rather than losing it.
    /// </summary>
    Task RecordSyncOutcome(int gameCharacterId, string rsn, CollectionLogSyncOutcome outcome, string? error);

    /// <summary>Marks a skipped character as checked when Temple's last-changed hasn't moved.</summary>
    Task RecordUnchanged(int gameCharacterId, DateTimeOffset? templeLastChecked);
}
