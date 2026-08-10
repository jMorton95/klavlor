using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// The denominator behind a repeat drop's luck verdict on the live feed.
//
// Production bug: a repeat receipt was judged against its ABSOLUTE roll number, so a 1/100 item
// received at roll 50 and again at roll 200 reported "2x dry" on the second — when the player had
// actually gone 150 rolls since the last one. This is the query that supplies that 150.
[Collection("postgres")]
public sealed class RollsSincePreviousReceiptTests(PostgresFixture fx)
{
    [Fact]
    public async Task The_gap_is_measured_from_the_previous_receipt_not_from_the_start()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "gap-basic");
        var t = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
        const string source = "GAP_Vorkath";
        const string item = "GAP Visage";

        // 200 rolls. The item lands on roll 50 and again on roll 200 — the exact shape reported.
        for (var i = 1; i <= 200; i++)
        {
            var drops = i is 50 or 200
                ? new List<Domain.Entities.LootDrop> { new(item, 70_001, 1, 1_000) }
                : [new("GAP Bones", 70_002, 1, 100)];
            Seed.AddKill(ctx, userId, charId, source, t.AddMinutes(i), i, drops);
        }
        await ctx.SaveChangesAsync();

        var repo = new LootRecordRepository(ctx, NullLogger<LootRecordRepository>.Instance);

        var second = new ItemReceipt(charId, source, item, t.AddMinutes(200));
        var first = new ItemReceipt(charId, source, item, t.AddMinutes(50));
        var gaps = await repo.GetRollsSincePreviousReceipt([first, second]);

        // The second receipt: 150 rolls since the first, NOT 200.
        Assert.Equal(150, gaps[second]);
        // The first receipt has nothing before it, so it is absent — the caller falls back to the
        // absolute count, which for a first receipt is already the right basis.
        Assert.False(gaps.ContainsKey(first));
    }

    [Fact]
    public async Task Back_to_back_receipts_report_a_gap_of_one()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "gap-btb");
        var t = new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero);
        const string source = "GAP_Lunar";
        const string item = "GAP Dart";

        // A guaranteed drop: every roll yields it. Each receipt is one roll after the last.
        for (var i = 1; i <= 5; i++)
            Seed.AddKill(ctx, userId, charId, source, t.AddMinutes(i), i,
                [new(item, 70_011, 1, 500)]);
        await ctx.SaveChangesAsync();

        var repo = new LootRecordRepository(ctx, NullLogger<LootRecordRepository>.Instance);
        var gaps = await repo.GetRollsSincePreviousReceipt(
            [new ItemReceipt(charId, source, item, t.AddMinutes(5))]);

        Assert.Equal(1, gaps[new ItemReceipt(charId, source, item, t.AddMinutes(5))]);
    }

    [Fact]
    public async Task Gaps_are_per_item_and_per_source()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "gap-scope");
        var t = new DateTimeOffset(2026, 5, 3, 9, 0, 0, TimeSpan.Zero);
        const string sourceA = "GAP_SourceA";
        const string sourceB = "GAP_SourceB";
        const string itemX = "GAP ItemX";
        const string itemY = "GAP ItemY";

        // Source A: itemX on rolls 1 and 10, itemY on rolls 2 and 4.
        for (var i = 1; i <= 10; i++)
        {
            var drops = new List<Domain.Entities.LootDrop>();
            if (i is 1 or 10) drops.Add(new(itemX, 70_021, 1, 100));
            if (i is 2 or 4) drops.Add(new(itemY, 70_022, 1, 100));
            if (drops.Count == 0) drops.Add(new("GAP Filler", 70_023, 1, 10));
            Seed.AddKill(ctx, userId, charId, sourceA, t.AddMinutes(i), i, drops);
        }
        // Source B drops itemX too — it must not shorten source A's gap.
        for (var i = 1; i <= 3; i++)
            Seed.AddKill(ctx, userId, charId, sourceB, t.AddMinutes(i).AddSeconds(30), i,
                [new(itemX, 70_021, 1, 100)]);
        await ctx.SaveChangesAsync();

        var repo = new LootRecordRepository(ctx, NullLogger<LootRecordRepository>.Instance);

        var xSecond = new ItemReceipt(charId, sourceA, itemX, t.AddMinutes(10));
        var ySecond = new ItemReceipt(charId, sourceA, itemY, t.AddMinutes(4));
        var gaps = await repo.GetRollsSincePreviousReceipt([xSecond, ySecond]);

        Assert.Equal(9, gaps[xSecond]);   // rolls 2..10 at source A
        Assert.Equal(2, gaps[ySecond]);   // rolls 3..4 at source A
    }

    [Fact]
    public async Task Another_characters_receipts_never_count()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "gap-mine");
        var (otherUserId, otherCharId) = await Seed.UserAndCharacter(ctx, "gap-theirs");
        var t = new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);
        const string source = "GAP_Shared";
        const string item = "GAP Shared item";

        for (var i = 1; i <= 6; i++)
            Seed.AddKill(ctx, userId, charId, source, t.AddMinutes(i), i,
                i is 1 or 6 ? [new(item, 70_031, 1, 100)] : [new("GAP Junk", 70_032, 1, 10)]);
        // The other character grinds the same source and gets the same item in between.
        for (var i = 1; i <= 20; i++)
            Seed.AddKill(ctx, otherUserId, otherCharId, source, t.AddMinutes(i).AddSeconds(10), i,
                [new(item, 70_031, 1, 100)]);
        await ctx.SaveChangesAsync();

        var repo = new LootRecordRepository(ctx, NullLogger<LootRecordRepository>.Instance);
        var mine = new ItemReceipt(charId, source, item, t.AddMinutes(6));
        var gaps = await repo.GetRollsSincePreviousReceipt([mine]);

        Assert.Equal(5, gaps[mine]);
    }

    [Fact]
    public async Task An_empty_request_does_no_work()
    {
        await using var ctx = fx.CreateContext();
        var repo = new LootRecordRepository(ctx, NullLogger<LootRecordRepository>.Instance);

        Assert.Empty(await repo.GetRollsSincePreviousReceipt([]));
    }
}
