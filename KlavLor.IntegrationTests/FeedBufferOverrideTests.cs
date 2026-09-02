using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.ItemValues;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using KlavLor.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// THE LOOT FEED'S SWIMLANES ARE AN IN-MEMORY BUFFER, NOT A QUERY, and an item-value override changes
// what a stored drop is worth. Setting one used to re-price the database and leave the buffer alone.
//
// The symptom that reported it: an untradeable is priced by RuneLite at 0 GP, so it sits under the
// feed's 10k floor and never reaches a lane at all. The admin gives it an intrinsic value; the
// database is re-derived, so a PAGE LOAD — which reads the database — shows every past receipt
// correctly. The buffer still holds no entry for them. The next live kill at that source merges
// against that stale buffer and broadcasts a card carrying only itself. On screen the card appeared
// to refuse the new item: its roll count and KC range climbed, because those are ordinals resolved
// fresh on every publish, while its chips and total stood still. Restarting "fixed" it, which is
// exactly what a stale in-memory buffer looks like from the outside.
//
// FeedBufferSeeder is the shared pass that startup and the item-value admin both run. The second
// test drives the REAL admin handler, so deleting its Reseed call fails the suite rather than only
// failing in production three weeks later.
[Collection("postgres")]
public sealed class FeedBufferOverrideTests(PostgresFixture fx)
{
    // The Postgres container is shared across the collection, so each test owns its own source and
    // item id: an override row is unique per item, and the lane is read by source name.
    private const string LaneSource = "FBO_Unsired_Lane";
    private const int LaneItem = 93_001;         // RuneLite reports 0 GP for it
    private const string HandlerSource = "FBO_Unsired_Handler";
    private const int HandlerItem = 93_002;

    private const int OverrideValue = 5_000_000; // Rare: 1M–10M

    private sealed record Rig(
        FeedBufferSeeder Seeder,
        ILootFeedService Feed,
        FakeItemValueCache Cache,
        ItemValueOverrideRepository Overrides,
        DataContext Ctx);

    // Rates play no part in which lane a drop lands in; this is about the price.
    private sealed class NoModifiers : ISourceRateModifierCache
    {
        public double GetMultiplier(string sourceName, string? itemName) => 1.0;
        public void Replace(IEnumerable<SourceRateModifierValue> modifiers) { }
    }

    // RecomputeTrigger only flags a job for the poller; nothing here reads it back.
    private sealed class FakeJobSchedules : IJobScheduleRepository
    {
        public Task<bool> TryClaim(string jobName, TimeSpan interval) => Task.FromResult(false);
        public Task RequestManual(string jobName) => Task.CompletedTask;
        public Task<IReadOnlyCollection<string>> GetPendingJobNames() =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    private static Rig Build(DataContext ctx)
    {
        var cache = new FakeItemValueCache();
        var feedRepo = new LootFeedRepository(ctx, NullLogger<LootFeedRepository>.Instance, new FakeClogCache(), cache);
        var tiers = new LootFeedTiersHandler(
            feedRepo,
            new DropRateRepository(ctx, NullLogger<DropRateRepository>.Instance),
            new CharacterDelveDepthRepository(ctx),
            new SourceLootService([new DefaultSourceLootStrategy()], new NoModifiers()));

        var feed = new LootFeedService(new LootFeedHighlightTracker());
        return new Rig(
            new FeedBufferSeeder(tiers, feed),
            feed,
            cache,
            new ItemValueOverrideRepository(ctx, cache, new FakeClogCache()),
            ctx);
    }

    private static LootFeedEntry? Card(ILootFeedService feed, string source) =>
        feed.GetCurrentEntries(LootFeedScope.Main, LootFeedTier.Rare)
            .SingleOrDefault(e => e.SourceName == source);

    [Fact]
    public async Task An_override_puts_a_previously_worthless_drop_into_its_lane()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "fbo-lane");
        var t = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        Seed.AddKill(ctx, userId, charId, LaneSource, t, 1, [new("FBO Lane claw", LaneItem, 1, 0)]);
        Seed.AddKill(ctx, userId, charId, LaneSource, t.AddHours(1), 2, [new("FBO Lane spine", LaneItem, 1, 0)]);
        await ctx.SaveChangesAsync();

        var rig = Build(ctx);

        // At 0 GP it is under the feed's floor, so nothing is in any lane. This is the state the
        // buffer was stuck in, and the state a restart would have left it in too.
        await rig.Seeder.Reseed();
        Assert.Null(Card(rig.Feed, LaneSource));

        rig.Ctx.ItemValueOverrides.Add(new ItemValueOverride
        {
            ItemId = LaneItem,
            ItemName = "FBO Lane claw",
            Value = OverrideValue
        });
        await rig.Ctx.SaveChangesAsync();
        rig.Cache.Replace([new ItemValueOverrideValue(LaneItem, "FBO Lane claw", OverrideValue)]);
        await rig.Overrides.RebuildForItem(LaneItem);

        await rig.Seeder.Reseed();

        var card = Card(rig.Feed, LaneSource);
        Assert.NotNull(card);
        Assert.Equal(2, card!.Drops.Count);
        Assert.Equal(2 * OverrideValue, card.TotalValue);
    }

    [Fact]
    public async Task Setting_a_value_through_the_admin_handler_refreshes_the_buffer_and_later_drops_merge_onto_it()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "fbo-handler");
        var t = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);

        Seed.AddKill(ctx, userId, charId, HandlerSource, t, 1, [new("FBO Handler claw", HandlerItem, 1, 0)]);
        Seed.AddKill(ctx, userId, charId, HandlerSource, t.AddHours(1), 2, [new("FBO Handler spine", HandlerItem, 1, 0)]);
        await ctx.SaveChangesAsync();

        var rig = Build(ctx);

        // Startup, before anybody set a value: the buffer has no entry for this source.
        await rig.Seeder.Reseed();
        Assert.Null(Card(rig.Feed, HandlerSource));

        // The REAL admin write — not reaching past it to the seeder.
        var handler = new ItemValueOverrideAdminHandler(
            rig.Overrides,
            rig.Cache,
            new MemoryCache(new MemoryCacheOptions()),
            new RecomputeTrigger(new FakeJobSchedules()),
            rig.Seeder);

        var result = await handler.Set(HandlerItem, "FBO Handler claw", OverrideValue);
        Assert.True(result.IsSuccess);

        // THE FIX: the write alone brings the buffer up to date. Without the handler's Reseed call
        // this is still null, and the lane stays empty until the next restart.
        var seeded = Card(rig.Feed, HandlerSource);
        Assert.NotNull(seeded);
        Assert.Equal(2, seeded!.Drops.Count);

        // ...and the reported symptom: a later live kill merges ONTO those receipts instead of
        // replacing the card with a one-drop version of itself.
        rig.Feed.Publish(new LootFeedEntry(
            "Test User", userId, HandlerSource, LootSourceType.Npc,
            OverrideValue,
            [new LootFeedDrop("FBO Handler head", 1, OverrideValue)],
            t.AddHours(2),
            LootFeedTier.Rare,
            CharacterName: "fbo-handler",
            GameCharacterId: charId));

        var merged = Card(rig.Feed, HandlerSource);
        Assert.NotNull(merged);
        Assert.Equal(3, merged!.Drops.Count);
        Assert.Equal(3 * OverrideValue, merged.TotalValue);
        Assert.Equal(
            ["FBO Handler claw", "FBO Handler head", "FBO Handler spine"],
            merged.Drops.Select(d => d.Name).Order().ToArray());
    }
}
