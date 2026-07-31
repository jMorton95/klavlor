using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// The "N sources · mostly X" label on the search Drops cards (and "mostly from X" on the
// character profile's top items) used to rank the top source by total GP value with no
// tie-break. Two bugs fell out of that: the label says "mostly" but reported the most
// *valuable* source rather than the most *frequent* one, and on ties — which every
// zero-price item hits, since all its sources tie at 0 — the winner came out in the
// group-aggregate's input order, i.e. alphabetically, so a source starting with "A" always
// won. Both queries now rank by occurrence count with source name as an explicit final
// tie-break; these tests pin that.
[Collection("postgres")]
public sealed class SearchDropsTopSourceTests(PostgresFixture fx)
{
    [Fact]
    public async Task Top_source_is_the_most_frequent_source_not_the_most_valuable_one()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "topsrc");
        var at = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        // "TS_Zulrah" drops the item three times for 300gp total; "TS_Aberrant" drops it once
        // but for 10_000gp. Most frequent = Zulrah, most valuable = Aberrant. Aberrant is also
        // alphabetically first, so ranking by either value or name picks the wrong source.
        var item = "TSUnique blade";
        for (var i = 0; i < 3; i++)
            Seed.AddKill(ctx, userId, charId, "TS_Zulrah", at.AddMinutes(i), i + 1, [new(item, 9001, 1, 100)]);
        Seed.AddKill(ctx, userId, charId, "TS_Aberrant", at.AddHours(1), 1, [new(item, 9001, 1, 10_000)]);
        await ctx.SaveChangesAsync();

        var repo = new SearchRepository(ctx, NullLogger<SearchRepository>.Instance);
        var rows = await repo.SearchDrops("TSUnique", 20);

        var row = Assert.Single(rows, r => r.ItemName == item);
        Assert.Equal(2, row.SourceCount);
        Assert.Equal("TS_Zulrah", row.TopSourceName);
    }

    [Fact]
    public async Task Zero_price_item_does_not_fall_back_to_the_alphabetically_first_source()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "topsrczero");
        var at = new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero);

        // Price 0 for every drop, so total value ties at 0 across both sources. The old query
        // returned "TZ_Abyssal" purely because it sorts first by name.
        var item = "TZPet rock";
        for (var i = 0; i < 4; i++)
            Seed.AddKill(ctx, userId, charId, "TZ_Zulrah", at.AddMinutes(i), i + 1, [new(item, 9002, 1, 0)]);
        Seed.AddKill(ctx, userId, charId, "TZ_Abyssal", at.AddHours(1), 1, [new(item, 9002, 1, 0)]);
        await ctx.SaveChangesAsync();

        var repo = new SearchRepository(ctx, NullLogger<SearchRepository>.Instance);
        var rows = await repo.SearchDrops("TZPet", 20);

        var row = Assert.Single(rows, r => r.ItemName == item);
        Assert.Equal("TZ_Zulrah", row.TopSourceName);
    }

    [Fact]
    public async Task Profile_top_items_ranks_its_top_source_by_frequency_too()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "toptop");
        var at = new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.Zero);

        var item = "TPUnique horn";
        for (var i = 0; i < 3; i++)
            Seed.AddKill(ctx, userId, charId, "TP_Zulrah", at.AddMinutes(i), i + 1, [new(item, 9003, 1, 100)]);
        Seed.AddKill(ctx, userId, charId, "TP_Aberrant", at.AddHours(1), 1, [new(item, 9003, 1, 10_000)]);
        await ctx.SaveChangesAsync();

        var repo = new LootLogRepository(ctx, NullLogger<LootLogRepository>.Instance, new FakeClogCache());
        var top = await repo.GetTopItems(charId, 20);

        var row = Assert.Single(top.Items, r => r.ItemName == item);
        Assert.Equal(2, row.SourceCount);
        Assert.Equal("TP_Zulrah", row.TopSourceName);
    }
}
