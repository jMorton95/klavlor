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

// Top-level partition for the live feed. Main = standard OSRS characters,
// Leagues = OSRS seasonal Leagues characters. Kept separate so leagues activity
// doesn't drown out the main feed during a league season.
public enum LootFeedScope
{
    Main,
    Leagues
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

    /// <summary>
    /// True when a card's TOTAL is worth more than the lane it sits in — several drops that each
    /// classify into this tier, summing past its ceiling. Three 90M drops from one source land as
    /// one Epic card worth 270M, which is Legendary money sitting in the Epic lane.
    ///
    /// This does NOT contradict "tiers are per drop": the lane is still chosen by the single
    /// receipt (<see cref="GetDropTier"/>), and a card is never promoted out of its lane. The
    /// answer only drives a presentation cue - the card pulses in its OWN tier colour - so a big
    /// session reads as big without a stack of cheap drops ever being able to fake a rarer one.
    ///
    /// Always false for Legendary, which has no ceiling to exceed.
    /// </summary>
    static bool ExceedsTier(LootFeedTier tier, long cardTotal) =>
        GetDropTier(cardTotal) is { } totalTier && totalTier > tier;

    static readonly LootFeedTier[] AllTiers =
        [LootFeedTier.Standard, LootFeedTier.Uncommon, LootFeedTier.Rare, LootFeedTier.Epic, LootFeedTier.Legendary];

    static Dictionary<LootFeedTier, List<LootFeedDrop>> ClassifyDropsByTier(List<LootFeedDrop> drops)
    {
        var result = new Dictionary<LootFeedTier, List<LootFeedDrop>>();
        foreach (var drop in drops)
        {
            // Admin-injected untradeables have no value, so they'd never tier by GP — force them
            // into the top lane so the giga effect can render.
            var tier = drop.IsSpecial ? LootFeedTier.Legendary : GetDropTier((long)drop.Quantity * drop.Price);
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

    IReadOnlyList<LootFeedEntry> GetCurrentEntries(LootFeedScope scope, LootFeedTier tier);
    IAsyncEnumerable<LootFeedBroadcast> SubscribeAsync(LootFeedScope scope, LootFeedTier tier, CancellationToken cancellationToken);
    void Publish(LootFeedEntry entry);
    void SeedBuffer(LootFeedScope scope, IEnumerable<LootFeedEntry> entries);
}
