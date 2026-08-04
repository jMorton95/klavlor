using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Features.Loot.Feed;

public sealed class RecentSessionsHandler(ILootFeedRepository feedRepository, IMemoryCache cache)
{
    public const int WindowHours = 48;

    // Cached briefly and shared across every viewer. The query does two gap-and-islands passes over
    // 64 hours of records for all characters at once, which is far too much to repeat per popover
    // click — and the answer is the same for everyone, so there's nothing per-user to key on. A
    // short TTL keeps it feeling live: worst case the panel is a minute behind the swimlanes, and
    // the swimlanes are the surface that has to be instant.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);

    public async Task<RecentSessionsPanel> Handle(LootFeedScope scope)
    {
        var key = $"loot:recent-sessions:{scope}:{WindowHours}";
        var panel = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await feedRepository.GetRecentSessions(WindowHours, scope);
        });
        return panel!;
    }
}
