using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

[Collection("postgres")]
public sealed class SourceTableTests(PostgresFixture fx)
{
    [Fact]
    public async Task Source_table_aggregates_per_source_with_totals_and_sorts()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "srctable");
        var t = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        // Visage (ItemId 2) is a real collection-log item mapped to the Vorkath tab, so the
        // SQL clog-unlocked / clog-total columns (which read EffectiveCollectionLogItems)
        // resolve. (FakeClogCache only covers the in-memory NEW-badge path.)
        Seed.AddClogItem(ctx, 2, "Visage", "ST_Vorkath");

        // Vorkath: 2 kills, one big drop (clog first-time), two sessions (a >1h gap).
        Seed.AddKill(ctx, userId, charId, "ST_Vorkath", t, 1, [new("Visage", 2, 1, 50_000_000, IsFirstTime: true)]);
        Seed.AddKill(ctx, userId, charId, "ST_Vorkath", t.AddHours(2), 2, [new("Bones", 1, 1, 100)]);
        // Zulrah: 1 kill, smaller value.
        Seed.AddKill(ctx, userId, charId, "ST_Zulrah", t.AddMinutes(30), 1, [new("Scale", 3, 100, 10)]);
        await ctx.SaveChangesAsync();

        var repo = new LootLogRepository(ctx, NullLogger<LootLogRepository>.Instance, new FakeClogCache(2));

        // Default sort = value desc → Vorkath first (50M » 1K).
        var byValue = await repo.GetCharacterSourceTable(charId, new LootLogQuery(PageSize: 20, PageNumber: 1));
        Assert.Equal(2, byValue.TotalSources);
        Assert.Equal("ST_Vorkath", byValue.Rows[0].SourceName);
        var vork = byValue.Rows[0];
        Assert.Equal(2, vork.Kills);
        Assert.Equal(50_000_100, vork.TotalValue);
        Assert.Equal(2, vork.Sessions);                 // two runs separated by >1h
        Assert.Equal(2, vork.DistinctItems);            // Visage + Bones
        Assert.Equal("Visage", vork.BiggestDropName);
        Assert.Equal(50_000_000, vork.BiggestDropValue);
        Assert.Equal(1, vork.ClogUnlocked);             // Visage is a first-time clog item

        // Totals span every matching source.
        Assert.Equal(2, byValue.Totals.Sources);
        Assert.Equal(3, byValue.Totals.Kills);
        Assert.Equal(50_001_100, byValue.Totals.TotalValue);

        // Sort by kills ascending → Zulrah (1) before Vorkath (2).
        var byKills = await repo.GetCharacterSourceTable(charId,
            new LootLogQuery(PageSize: 20, PageNumber: 1, SortBy: "kills", SortDirection: SortDirection.Ascending));
        Assert.Equal("ST_Zulrah", byKills.Rows[0].SourceName);
        Assert.Equal("ST_Vorkath", byKills.Rows[1].SourceName);

        // Filter narrows to one source.
        var filtered = await repo.GetCharacterSourceTable(charId,
            new LootLogQuery(PageSize: 20, PageNumber: 1, SearchTerm: "zulrah"));
        Assert.Single(filtered.Rows);
        Assert.Equal("ST_Zulrah", filtered.Rows[0].SourceName);
        Assert.Equal(1, filtered.Totals.Sources);
    }
}
