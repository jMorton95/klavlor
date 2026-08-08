using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Services;

namespace KlavLor.UnitTests;

// Intrinsic item values: the admin-set GP figure for items RuneLite prices at 0 because they are
// untradeable (the Noxious halberd's three components, each worth roughly 10m).
//
// The invariant these pin is that ItemValueOverrideCache is the ONLY place a raw price and an
// override are reconciled, and that the result flows through the ordinary tier classification —
// there is no special-casing of overridden items anywhere downstream. That is what makes "set it to
// 10 million and it lands in Epic, set it to 100 thousand and it lands in Uncommon" true by
// construction rather than by a second code path.
public sealed class ItemValueOverrideTests
{
    private const int NoxiousPoint = 25_896;
    private const int Shark = 385;

    private static IItemValueOverrideCache CacheWith(params (int ItemId, int Value)[] overrides)
    {
        var cache = new ItemValueOverrideCache();
        cache.Replace(overrides.Select(o => new ItemValueOverrideValue(o.ItemId, $"item-{o.ItemId}", o.Value)));
        return cache;
    }

    [Fact]
    public void An_unconfigured_item_keeps_its_raw_price()
    {
        var cache = CacheWith((NoxiousPoint, 10_000_000));

        Assert.Equal(1_000, cache.GetPrice(Shark, 1_000));
        Assert.Equal(0, cache.GetPrice(Shark, 0));
    }

    [Fact]
    public void A_configured_item_takes_the_override_regardless_of_its_raw_price()
    {
        var cache = CacheWith((NoxiousPoint, 10_000_000));

        // The case this feature exists for: RuneLite reports 0 because the item is untradeable.
        Assert.Equal(10_000_000, cache.GetPrice(NoxiousPoint, 0));
        // And the override still wins if a raw price does turn up — it is a flat global value, not
        // a fallback for missing data.
        Assert.Equal(10_000_000, cache.GetPrice(NoxiousPoint, 4_200));
    }

    [Fact]
    public void An_empty_cache_reports_nothing_configured_so_hot_paths_can_skip_the_rewrite()
    {
        var cache = CacheWith();

        Assert.False(cache.HasAny);
        Assert.Equal(1_234, cache.GetPrice(NoxiousPoint, 1_234));
    }

    [Fact]
    public void Replace_drops_overrides_that_are_no_longer_configured()
    {
        var cache = new ItemValueOverrideCache();
        cache.Replace([new ItemValueOverrideValue(NoxiousPoint, "Noxious point", 10_000_000)]);
        Assert.Equal(10_000_000, cache.GetPrice(NoxiousPoint, 0));

        // Removal restores the raw price — which is what makes an override reversible, because
        // DropsJson never stopped holding the figure RuneLite actually sent.
        cache.Replace([]);
        Assert.False(cache.HasAny);
        Assert.Equal(0, cache.GetPrice(NoxiousPoint, 0));
    }

    [Fact]
    public void Re_pricing_a_drop_list_rewrites_only_the_overridden_entries()
    {
        var cache = CacheWith((NoxiousPoint, 10_000_000));
        var drops = new List<LootDrop>
        {
            new("Noxious point", NoxiousPoint, Quantity: 1, Price: 0),
            new("Shark", Shark, Quantity: 20, Price: 1_000)
        };

        var priced = cache.WithEffectivePrices(drops);

        Assert.Equal(10_000_000, priced[0].Price);
        Assert.Equal(1_000, priced[1].Price);
        // Everything other than the price is carried through untouched.
        Assert.Equal("Noxious point", priced[0].Name);
        Assert.Equal(1, priced[0].Quantity);
        // The input list is never mutated — the caller's canonical DropsJson view stays raw.
        Assert.Equal(0, drops[0].Price);
    }

    [Fact]
    public void Re_pricing_returns_the_same_list_when_nothing_is_overridden()
    {
        var cache = CacheWith();
        var drops = new List<LootDrop> { new("Shark", Shark, 1, 1_000) };

        Assert.Same(drops, cache.WithEffectivePrices(drops));
    }

    [Theory]
    // The admin's stated expectation: the value alone decides the swimlane, through the same
    // GetDropTier every other drop goes through.
    [InlineData(10_000_000, LootFeedTier.Epic)]
    [InlineData(100_000, LootFeedTier.Uncommon)]
    [InlineData(1_000_000, LootFeedTier.Rare)]
    [InlineData(100_000_000, LootFeedTier.Legendary)]
    public void An_overridden_item_is_classified_by_its_override_value(int value, LootFeedTier expected)
    {
        var cache = CacheWith((NoxiousPoint, value));
        var drop = cache.WithEffectivePrices([new LootDrop("Noxious point", NoxiousPoint, 1, 0)])[0];

        Assert.Equal(expected, ILootFeedService.GetDropTier((long)drop.Quantity * drop.Price));
    }

    [Fact]
    public void Without_an_override_an_untradeable_never_reaches_the_feed_at_all()
    {
        // The status quo this feature fixes: priced at 0, it sits below the 10K publish floor.
        var cache = CacheWith();
        var drop = cache.WithEffectivePrices([new LootDrop("Noxious point", NoxiousPoint, 1, 0)])[0];

        Assert.Null(ILootFeedService.GetDropTier((long)drop.Quantity * drop.Price));
    }
}
