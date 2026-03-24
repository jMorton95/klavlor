using KlavLor.Application.Features.Loot.Feed;

namespace KlavLor.Application.Interfaces.Services;

public enum LootFeedTier
{
    Standard,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public interface ILootFeedService
{
    static LootFeedTier? GetDropTier(long dropValue) => dropValue switch
    {
        >= 100_000_000 => LootFeedTier.Legendary,
        >= 10_000_000 => LootFeedTier.Epic,
        >= 1_000_000 => LootFeedTier.Rare,
        >= 100_000 => LootFeedTier.Uncommon,
        >= 10_000 => LootFeedTier.Standard,
        _ => null
    };

    static (long Min, long? Max) GetTierRange(LootFeedTier tier) => tier switch
    {
        LootFeedTier.Standard => (10_000, 100_000),
        LootFeedTier.Uncommon => (100_000, 1_000_000),
        LootFeedTier.Rare => (1_000_000, 10_000_000),
        LootFeedTier.Epic => (10_000_000, 100_000_000),
        LootFeedTier.Legendary => (100_000_000, null),
        _ => throw new ArgumentOutOfRangeException(nameof(tier))
    };

    static readonly LootFeedTier[] AllTiers =
        [LootFeedTier.Standard, LootFeedTier.Uncommon, LootFeedTier.Rare, LootFeedTier.Epic, LootFeedTier.Legendary];

    static Dictionary<LootFeedTier, List<LootFeedDrop>> ClassifyDropsByTier(List<LootFeedDrop> drops)
    {
        var result = new Dictionary<LootFeedTier, List<LootFeedDrop>>();
        foreach (var drop in drops)
        {
            var tier = GetDropTier((long)drop.Quantity * drop.Price);
            if (tier is null) continue;
            if (!result.TryGetValue(tier.Value, out var list))
            {
                list = [];
                result[tier.Value] = list;
            }
            list.Add(drop);
        }
        return result;
    }

    IReadOnlyList<LootFeedEntry> GetCurrentEntries(LootFeedTier tier);
    IAsyncEnumerable<LootFeedEntry> SubscribeAsync(LootFeedTier tier, CancellationToken cancellationToken);
    void Publish(LootFeedEntry entry);
    void SeedBuffer(IEnumerable<LootFeedEntry> entries);
}
