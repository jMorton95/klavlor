using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Interfaces.Repositories;

// Everything that builds feed cards from stored records: the live feed's per-tier backfill, the
// character day feed, and the first-time (collection-log) feed. One of the five repositories
// ILootLogRepository was split into, grouped by consumer feature.
public interface ILootFeedRepository
{
    /// <param name="gameCharacterId">Restricts the backfill to one character. Null shows everyone.</param>
    Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> GetAllFeedTiers(int countPerTier, LootFeedScope scope = LootFeedScope.Main, IReadOnlySet<LootFeedTier>? requestedTiers = null, int? gameCharacterId = null);

    /// <summary>
    /// The characters the feed can be filtered to, for this scope. Deliberately a projection of a
    /// tiny table rather than whole entities: it is fetched once per feed load and the only thing
    /// needed is a value and a label.
    /// </summary>
    Task<List<FeedCharacterOption>> GetFeedCharacters(LootFeedScope scope);
    Task<CharacterDayFeed> GetCharacterDayFeed(int characterId, DateOnly day);
    Task<FirstTimeFeed> GetFirstTimeFeed(int characterId, DateTimeOffset? before, int pageSize);

    /// <summary>
    /// Notable play sessions across every visible character in the last <paramref name="windowHours"/>.
    /// </summary>
    Task<RecentSessionsPanel> GetRecentSessions(int windowHours, LootFeedScope scope);
}

public sealed record FeedCharacterOption(int Id, string Name);
