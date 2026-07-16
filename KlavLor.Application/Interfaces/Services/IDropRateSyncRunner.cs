namespace KlavLor.Application.Interfaces.Services;

// Fetches and stores the drop rates for a single source from the wiki. Shared by the
// background DropRateSyncService and the admin "fetch now" action.
public interface IDropRateSyncRunner
{
    Task<DropRateSyncResult> SyncSource(string sourceName, CancellationToken cancellationToken = default);
}

public sealed record DropRateSyncResult(string SourceName, int RatesStored, DropRateSyncOutcome Outcome)
{
    public bool FoundWikiData => Outcome == DropRateSyncOutcome.Synced;
}

public enum DropRateSyncOutcome
{
    /// <summary>The wiki returned drops and they were stored.</summary>
    Synced,

    /// <summary>The wiki query succeeded but the source genuinely has no drops (marked as a miss).</summary>
    NoData,

    /// <summary>The fetch failed (network / API error); existing rows were kept for a later retry.</summary>
    FetchFailed
}
