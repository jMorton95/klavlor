using Microsoft.Extensions.Caching.Memory;

namespace KlavLor.Application.Features.Loot.Log;

/// Versioned cache for character drop-log stats. The version is bumped on
/// ingest so cached entries become unreachable instead of needing key enumeration.
internal static class LootStatsCache
{
    public static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VersionTtl = TimeSpan.FromDays(1);

    public static long GetVersion(IMemoryCache cache, int characterId)
        => cache.TryGetValue(VersionKey(characterId), out long v) ? v : 0L;

    public static void Invalidate(IMemoryCache cache, int characterId)
    {
        var key = VersionKey(characterId);
        var current = cache.TryGetValue(key, out long v) ? v : 0L;
        cache.Set(key, current + 1L, VersionTtl);
    }

    public static string EntryKey(int characterId, long version, string method, string args = "")
        => args.Length == 0
            ? $"loot-stats:{method}:v{version}:{characterId}"
            : $"loot-stats:{method}:v{version}:{characterId}:{args}";

    private static string VersionKey(int characterId) => $"loot-stats:ver:{characterId}";
}
