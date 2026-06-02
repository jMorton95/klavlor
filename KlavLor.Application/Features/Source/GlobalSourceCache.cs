using Microsoft.Extensions.Caching.Memory;

namespace KlavLor.Application.Features.Source;

/// Versioned cache for the global (all-players) source aggregates. A single global
/// version is bumped on any loot ingest, so every cached source entry becomes
/// unreachable at once instead of needing key enumeration. Mirrors LootStatsCache,
/// but the source view spans all characters so the version is not per-character.
internal static class GlobalSourceCache
{
    public static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VersionTtl = TimeSpan.FromDays(1);
    private const string VersionKey = "source-stats:ver";

    public static long GetVersion(IMemoryCache cache)
        => cache.TryGetValue(VersionKey, out long v) ? v : 0L;

    public static void Invalidate(IMemoryCache cache)
    {
        var current = cache.TryGetValue(VersionKey, out long v) ? v : 0L;
        cache.Set(VersionKey, current + 1L, VersionTtl);
    }

    public static string EntryKey(long version, string method, string sourceName)
        => $"source-stats:{method}:v{version}:{sourceName}";
}
