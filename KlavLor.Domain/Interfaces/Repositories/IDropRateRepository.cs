using KlavLor.Domain.Entities;

namespace KlavLor.Domain.Interfaces.Repositories;

public interface IDropRateRepository
{
    /// <summary>Transactionally replaces all drop-rate rows for one source. No-op when <paramref name="rates"/> is empty (never clobbers existing data on a failed sync).</summary>
    Task ReplaceForSource(string sourceName, IReadOnlyCollection<DropRate> rates);

    /// <summary>All distinct source names that any character has loot for (regardless of whether we've synced rates for them yet).</summary>
    Task<IReadOnlyList<string>> GetKnownSourceNames();

    /// <summary>(sourceName, MAX SyncedAt) pairs for the subset of <paramref name="knownSourceNames"/> that already have synced rates. Drives backlog-first ordering in the sync service.</summary>
    Task<IReadOnlyDictionary<string, DateTimeOffset>> GetLastSyncedAtBySource(IReadOnlyCollection<string> knownSourceNames);

    /// <summary>(sourceName, number of stored drop-rate rows) for every source that has any. Used by the admin drop-rate panel to flag sources missing rates.</summary>
    Task<IReadOnlyDictionary<string, int>> GetRateCountsBySource();

    /// <summary>Collection-log items that have no linked drop rate (no DropRate row resolved to their ItemId) — i.e. clog slots showing no rate on the site. Used by the admin audit.</summary>
    Task<IReadOnlyList<CollectionLogItem>> GetClogItemsMissingRates(int limit);

    /// <summary>Total count of collection-log items with no linked drop rate.</summary>
    Task<int> CountClogItemsMissingRates();

    /// <summary>(sources with rates, total rate rows, most recent SyncedAt) for the admin sync-health panel.</summary>
    Task<(int SourceCount, int RateCount, DateTimeOffset? LastSynced)> GetStatus();

    /// <summary>Records that a source has no wiki drop-rate data (idempotent).</summary>
    Task MarkNoWikiData(string sourceName);

    /// <summary>Clears the "no wiki data" mark for a source (e.g. once a fetch finds data).</summary>
    Task ClearNoWikiData(string sourceName);

    /// <summary>All source names currently marked as having no wiki drop-rate data.</summary>
    Task<IReadOnlyList<string>> GetNoWikiDataSources();

    /// <summary>
    /// The drop rate for a (source, item) pair (item matched case-insensitively). When a source
    /// lists the item under several variants, prefers a row with a parsed numeric rarity. Null
    /// when no rate is stored. Used by loot auto-completion to judge how lucky/dry a drop was.
    /// </summary>
    Task<DropRate?> GetRate(string sourceName, string itemName);
}
