using Microsoft.Extensions.Caching.Memory;

namespace KlavLor.Application.Features.Source;

/// Versioned cache for the global (all-players) source aggregates. The version is bumped
/// per source name, so an ingest only invalidates the sources it actually touched instead
/// of busting every cached source at once (RuneLite ingests continuously, so a single
/// global version would rarely survive its TTL). Mirrors LootStatsCache, but keyed by
/// source name rather than character id.
///
/// NOTE: this is an in-process IMemoryCache. Production runs a single replica today; if it
/// is ever scaled to >1 replica, the per-source version lives in each replica's memory and
/// would need a shared/distributed backing store (or sticky routing) to stay coherent.
internal static class GlobalSourceCache
{
    public static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VersionTtl = TimeSpan.FromDays(1);

    private static string VersionKey(string sourceName) => $"source-stats:ver:{sourceName}";

    public static long GetVersion(IMemoryCache cache, string sourceName)
        => cache.TryGetValue(VersionKey(sourceName), out long v) ? v : 0L;

    public static void Invalidate(IMemoryCache cache, string sourceName)
    {
        var key = VersionKey(sourceName);
        var current = cache.TryGetValue(key, out long v) ? v : 0L;
        cache.Set(key, current + 1L, VersionTtl);
    }

    public static string EntryKey(long version, string method, string sourceName)
        => $"source-stats:{method}:v{version}:{sourceName}";
}
