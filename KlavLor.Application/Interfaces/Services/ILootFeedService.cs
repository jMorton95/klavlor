using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.Application.Interfaces.Services;

public enum LootFeedTier
{
    Standard,
    Notable,
    Mega
}

public interface ILootFeedService
{
    static LootFeedTier GetTier(long totalValue) => totalValue switch
    {
        >= 1_000_000 => LootFeedTier.Mega,
        >= 100_000 => LootFeedTier.Notable,
        _ => LootFeedTier.Standard
    };

    IReadOnlyList<LootFeedEntry> GetCurrentEntries(LootFeedTier tier);
    IAsyncEnumerable<LootFeedEntry> SubscribeAsync(LootFeedTier tier, CancellationToken cancellationToken);
    void Publish(LootFeedEntry entry);
    void SeedBuffer(IEnumerable<LootFeedEntry> entries);
}
