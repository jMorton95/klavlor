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
