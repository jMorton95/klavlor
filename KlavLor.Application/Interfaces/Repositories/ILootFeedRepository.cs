using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Interfaces.Repositories;

// Everything that builds feed cards from stored records: the live feed's per-tier backfill, the
// character day feed, and the first-time (collection-log) feed. One of the five repositories
// ILootLogRepository was split into, grouped by consumer feature.
public interface ILootFeedRepository
{
    Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> GetAllFeedTiers(int countPerTier, LootFeedScope scope = LootFeedScope.Main, IReadOnlySet<LootFeedTier>? requestedTiers = null);
    Task<CharacterDayFeed> GetCharacterDayFeed(int characterId, DateOnly day);
    Task<FirstTimeFeed> GetFirstTimeFeed(int characterId, DateTimeOffset? before, int pageSize);

    /// <summary>
    /// Notable play sessions across every visible character in the last <paramref name="windowHours"/>.
    /// </summary>
    Task<RecentSessionsPanel> GetRecentSessions(int windowHours, LootFeedScope scope);
}
