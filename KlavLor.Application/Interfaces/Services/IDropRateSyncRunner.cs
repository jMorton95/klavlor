namespace KlavLor.Application.Interfaces.Services;

// Fetches and stores the drop rates for a single source from the wiki. Shared by the
// background DropRateSyncService and the admin "fetch now" action.
public interface IDropRateSyncRunner
{
    Task<DropRateSyncResult> SyncSource(string sourceName, CancellationToken cancellationToken = default);
}

public sealed record DropRateSyncResult(string SourceName, int RatesStored, bool FoundWikiData);
