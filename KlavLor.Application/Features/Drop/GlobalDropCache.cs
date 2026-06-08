using Microsoft.Extensions.Caching.Memory;

namespace KlavLor.Application.Features.Drop;

/// Versioned cache for the global (all-players) drop aggregates. The version is bumped per
/// item name, so an ingest only invalidates the items it actually touched instead of busting
/// every cached item at once (RuneLite ingests continuously, so a single global version would
/// rarely survive its TTL). Mirrors GlobalSourceCache, but keyed by item name rather than
/// source name.
///
/// NOTE: this is an in-process IMemoryCache. Production runs a single replica today; if it is
/// ever scaled to >1 replica, the per-item version lives in each replica's memory and would
/// need a shared/distributed backing store (or sticky routing) to stay coherent.
internal static class GlobalDropCache
{
    public static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VersionTtl = TimeSpan.FromDays(1);

    private static string VersionKey(string itemName) => $"drop-stats:ver:{itemName}";

    public static long GetVersion(IMemoryCache cache, string itemName)
        => cache.TryGetValue(VersionKey(itemName), out long v) ? v : 0L;

    public static void Invalidate(IMemoryCache cache, string itemName)
    {
        var key = VersionKey(itemName);
        var current = cache.TryGetValue(key, out long v) ? v : 0L;
        cache.Set(key, current + 1L, VersionTtl);
    }

    public static string EntryKey(long generation, long version, string method, string itemName)
        => $"drop-stats:{method}:g{generation}:v{version}:{itemName}";
}
