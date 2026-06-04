using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

[Collection("postgres")]
public sealed class LootDropProjectionTests(PostgresFixture fx)
{
    [Fact]
    public async Task Dual_write_keeps_LootDrops_in_sync_with_DropsJson()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dualwrite");

        var drops = new List<LootDrop>
        {
            new("Skeletal visage", 22006, 1, 35_000_000, IsFirstTime: true),
            new("Coins", 995, 1000, 1)
        };
        var rec = Seed.AddKill(ctx, userId, charId, "DW_Vorkath",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), killCount: 850, drops);
        await ctx.SaveChangesAsync();

        await using var verify = fx.CreateContext();
        var rows = await verify.LootDrops.Where(d => d.LootRecordId == rec.Id).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r is { Name: "Skeletal visage", Quantity: 1, IsFirstTime: true });
        Assert.Contains(rows, r => r is { Name: "Coins", Quantity: 1000, IsFirstTime: false });
    }

    [Fact]
    public async Task RebuildDropsForCharacter_reconstructs_rows_from_canonical_DropsJson()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "rebuild");

        // Insert a kill with DropsJson but no projected rows — simulates pre-backfill /
        // drifted data. The projection must be reconstructable from DropsJson alone.
        var drops = new List<LootDrop>
        {
            new("Dragon dagger", 1215, 1, 17_000),
            new("Coins", 995, 500, 1)
        };
        var rec = Seed.AddKill(ctx, userId, charId, "RB_Src", DateTimeOffset.UnixEpoch,
            killCount: null, drops, projectRows: false);
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await ctx.LootDrops.CountAsync(d => d.LootRecordId == rec.Id));

        var repo = new LootRecordRepository(ctx, NullLogger<LootRecordRepository>.Instance);
        await repo.RebuildDropsForCharacter(charId);

        await using var verify = fx.CreateContext();
        var rows = await verify.LootDrops.Where(d => d.LootRecordId == rec.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r is { Name: "Dragon dagger", Quantity: 1, Price: 17_000 });
        Assert.Contains(rows, r => r is { Name: "Coins", Quantity: 500 });
    }

    [Fact]
    public async Task RecomputeFirstTimeFlags_keeps_projection_consistent_with_DropsJson()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "reflag");

        // Two kills of the same item; the later one is (wrongly) flagged first-time. After
        // recompute, the earliest occurrence should own the flag — in DropsJson AND LootDrops.
        Seed.AddKill(ctx, userId, charId, "RF_Src", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            null, [new("Visage", 100, 1, 5, IsFirstTime: true)]);
        Seed.AddKill(ctx, userId, charId, "RF_Src", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null, [new("Visage", 100, 1, 5, IsFirstTime: false)]);
        await ctx.SaveChangesAsync();

        var repo = new LootRecordRepository(ctx, NullLogger<LootRecordRepository>.Instance);
        await repo.RecomputeFirstTimeFlags(charId);

        await using var verify = fx.CreateContext();
        // Exactly one first-time row, and it belongs to the earliest kill.
        var firstTimeRows = await verify.LootDrops
            .Where(d => d.LootRecord!.GameCharacterId == charId && d.IsFirstTime)
            .Include(d => d.LootRecord)
            .ToListAsync();
        Assert.Single(firstTimeRows);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), firstTimeRows[0].LootRecord!.OccurredAt);

        // Two kills × one drop each ⇒ the projection still has exactly two rows after the
        // delete+reinsert rebuild (no duplication, no loss).
        var rowCount = await verify.LootDrops.CountAsync(d => d.LootRecord!.GameCharacterId == charId);
        Assert.Equal(2, rowCount);
    }
}
