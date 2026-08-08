using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using KlavLor.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

[Collection("postgres")]
public sealed class DropRateSyncTests(PostgresFixture fx)
{
    // Configurable stand-in for the wiki client: only the drop-rate fetch is exercised here.
    private sealed class FakeWikiClient(IReadOnlyList<WikiDropRate>? rates) : IOsrsWikiClient
    {
        public Task<IReadOnlyList<WikiDropRate>?> FetchDropRatesForSource(string wikiPageTitle) => Task.FromResult(rates);
        public Task<IReadOnlyList<CollectionLogItemData>> FetchCollectionLogItems() => Task.FromResult<IReadOnlyList<CollectionLogItemData>>([]);
        public Task<List<OsrsSearchResult>> SearchItems(string searchTerm, int limit = 10) => Task.FromResult(new List<OsrsSearchResult>());
    }

    // A drop rate a multi-source item (herb) now yields via Bucket — previously unavailable
    // because it lived behind a shared drop-table template the wikitext scraper couldn't see.
    // "1/32.4" is stored as the scaled integer ratio 10/324.
    [Fact]
    public async Task Synced_multi_source_rate_surfaces_on_the_collection_panel()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "brate");
        var src = "BR_Chaos_" + Guid.NewGuid().ToString("N")[..8];
        var ranarrId = 900_000 + Random.Shared.Next(90_000); // avoid colliding with other tests' clog ids
        Seed.AddClogItem(ctx, ranarrId, "Grimy ranarr weed", src);
        // The character has actually received the herb at this source (so it's a clog entry).
        Seed.AddKill(ctx, userId, charId, src, new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            50, [new LootDrop("Grimy ranarr weed", ranarrId, 1, 200)]);
        await ctx.SaveChangesAsync();

        var repo = new DropRateRepository(ctx, NullLogger<DropRateRepository>.Instance);
        var runner = new DropRateSyncRunner(repo, new FakeWikiClient(
            [new WikiDropRate("Grimy ranarr weed", "1-2", "1/32.4", 10, 324, 1, null)]));

        var result = await runner.SyncSource(src);
        Assert.Equal(DropRateSyncOutcome.Synced, result.Outcome);

        // Stored with the resolved clog ItemId and the scaled ratio.
        var stored = await ctx.DropRates.SingleAsync(d => d.SourceName == src);
        Assert.Equal(ranarrId, stored.ItemId);
        Assert.Equal(10, stored.RarityNumerator);
        Assert.Equal(324, stored.RarityDenominator);

        // ...and it now shows a rarity on the character source page's collection panel.
        var log = new LootSourceDetailRepository(ctx, NullLogger<LootSourceDetailRepository>.Instance, new FakeClogCache(ranarrId), new FakeItemValueCache());
        var collection = await log.GetSourceCollection(charId, src);
        var entry = collection.Entries.Single(e => e.ItemName == "Grimy ranarr weed");
        Assert.Equal("1/32.4", entry.Rarity);
        Assert.Equal(10, entry.RarityNumerator);
        Assert.Equal(324, entry.RarityDenominator);
    }

    // A failed fetch (null) must keep existing rows and not flag the source as missing; only a
    // genuine empty result marks it — so a transient blip during the mass backfill is harmless.
    [Fact]
    public async Task Fetch_failure_keeps_rows_while_genuine_empty_marks_missing()
    {
        await using var ctx = fx.CreateContext();
        var src = "BF_" + Guid.NewGuid().ToString("N")[..8];
        ctx.DropRates.Add(new DropRate
        {
            SourceName = src,
            ItemName = "Existing item",
            Rarity = "1/100",
            RarityNumerator = 1,
            RarityDenominator = 100,
            Rolls = 1,
            SyncedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
        await ctx.SaveChangesAsync();

        var repo = new DropRateRepository(ctx, NullLogger<DropRateRepository>.Instance);

        // null -> fetch failed: existing row retained, no miss recorded.
        var failed = await new DropRateSyncRunner(repo, new FakeWikiClient(null)).SyncSource(src);
        Assert.Equal(DropRateSyncOutcome.FetchFailed, failed.Outcome);
        Assert.True(await ctx.DropRates.AnyAsync(d => d.SourceName == src));
        Assert.False(await ctx.DropRateMisses.AnyAsync(d => d.SourceName == src));

        // empty -> genuinely no wiki data: source marked as a miss.
        var empty = await new DropRateSyncRunner(repo, new FakeWikiClient([])).SyncSource(src);
        Assert.Equal(DropRateSyncOutcome.NoData, empty.Outcome);
        Assert.True(await ctx.DropRateMisses.AnyAsync(d => d.SourceName == src));
    }
}
