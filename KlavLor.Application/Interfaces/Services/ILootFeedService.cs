using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.Application.Interfaces.Services;

public enum LootFeedTier
{
    Standard,
    Notable,
    Epic,
    Legendary
}

public interface ILootFeedService
{
    static LootFeedTier? GetTier(long totalValue) => totalValue switch
    {
        >= 10_000_000 => LootFeedTier.Legendary,
        >= 1_000_000 => LootFeedTier.Epic,
        >= 100_000 => LootFeedTier.Notable,
        >= 10_000 => LootFeedTier.Standard,
        _ => null
    };

    IReadOnlyList<LootFeedEntry> GetCurrentEntries(LootFeedTier tier);
    IAsyncEnumerable<LootFeedEntry> SubscribeAsync(LootFeedTier tier, CancellationToken cancellationToken);
    void Publish(LootFeedEntry entry);
    void SeedBuffer(IEnumerable<LootFeedEntry> entries);
}
