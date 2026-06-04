namespace KlavLor.Application.Features.Maintenance;

public enum IconKind { Item, Source }

// An icon that has exhausted its automatic fetch retries (FailCount >= 3) and is stuck
// without an image, surfaced for manual retry.
public sealed record FailedIcon(IconKind Kind, int Id, string Name, int FailCount, DateTimeOffset? LastAttemptAt);

// Backfill coverage for an icon table: total rows, how many resolved to an image, how
// many are still pending automatic retry, and how many are stuck (FailCount >= 3).
public sealed record IconStats(int Total, int Cached, int Pending, int Failed, DateTimeOffset? LastAttemptAt);

// Snapshot of the data-sync pipelines for the admin health panel.
public sealed record SyncStatus(
    int ClogItemCount,
    DateTimeOffset? ClogLastSynced,
    int DropRateSourceCount,
    int DropRateCount,
    DateTimeOffset? DropRateLastSynced,
    IconStats ItemIcons,
    IconStats SourceIcons,
    int CachedImageCount);

// A distinct loot-source name and how many records use it — for the rename/merge tool.
public sealed record SourceNameRow(string SourceName, long LootCount);

// Outcome of a rename/merge: loot records repointed from one name to another.
public sealed record SourceRenameResult(string From, string To, int MovedRecords);

// Read-only impact estimate for a proposed rename/merge, shown before the admin commits.
// IsMerge is true when the target name already has loot (so the two will be combined).
public sealed record SourceRenamePreview(
    string From,
    string To,
    int RecordsToMove,
    bool IsMerge,
    int TargetExistingRecords,
    int DropRatesAffected,
    bool HasIcon,
    bool IsNoop,
    string? NoopReason);
