using Microsoft.Extensions.Caching.Memory;

namespace KlavLor.Application.Common;

/// <summary>
/// Process-wide generation token folded into the global source- and drop-aggregate cache
/// keys. Loot ingest invalidates only the specific source/item it touched, but an admin
/// edit to the collection-log blacklist changes which drops count as collection-log
/// unlocks for <em>every</em> source and item — there is no single name to bump. Bumping
/// this generation makes every previously-cached aggregate entry unreachable at once, so an
/// excluded item stops appearing immediately instead of lingering until the 5-minute entry
/// TTL expires.
///
/// NOTE: like the per-name versions, this lives in the single replica's IMemoryCache; a
/// multi-replica deployment would need a shared/distributed backing store to stay coherent.
/// </summary>
internal static class AggregateCacheGeneration
{
    private const string Key = "loot-aggregate:generation";
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);

    public static long Get(IMemoryCache cache)
        => cache.TryGetValue(Key, out long g) ? g : 0L;

    public static void Bump(IMemoryCache cache)
        => cache.Set(Key, Get(cache) + 1L, Ttl);
}
