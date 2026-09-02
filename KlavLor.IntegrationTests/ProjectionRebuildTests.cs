using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// LootRecordRepository.RebuildDropsForCharacter rebuilds the LootDrops projection from the canonical
// DropsJson. Its whole job is to make the projection provably equal to the JSON again after a
// first-time reflag — but "equal to" is NOT "copied from", and getting that wrong shipped a bug.
//
// DropsJson holds the RAW RuneLite price by design (that is what makes an override reversible).
// LootDrops.Price is the DERIVED projection and must hold the EFFECTIVE price. A straight copy reset
// every overridden item back to the raw figure it had been overridden FOR — and it did so for the
// WHOLE character, on any imported batch or special-drop injection, silently, long after the admin
// had set the value and watched it take effect.
//
// The same copy dropped IsSpecial, which is how the feed's legendary lane finds admin-injected
// drops: SpecialLootHandler writes the flag and then calls RecomputeFirstTimeFlags, which un-wrote
// it on the very next statement.
[Collection("postgres")]
public sealed class ProjectionRebuildTests(PostgresFixture fx)
{
    // The container is shared across the collection, so every test owns distinct item ids.
    private const int OverriddenItem = 91_101;   // untradeable: RuneLite prices it at 0
    private const int PlainItem = 91_102;        // ordinary priced drop in the same kill
    private const int SpecialItem = 91_103;
    private const int ReflagItem = 91_104;

    private const int OverrideValue = 5_000_000;

    private static LootRecordRepository Repo(DataContext ctx) =>
        new(ctx, NullLogger<LootRecordRepository>.Instance);

    [Fact]
    public async Task Rebuilding_the_projection_keeps_the_item_value_override()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "rebuild-ovr");
        var t = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

        await AddOverride(ctx, OverriddenItem, "Rebuild Bludgeon claw", OverrideValue);

        // Ingest's own shape: DropsJson carries the RAW 0, the projected row carries the EFFECTIVE
        // price, exactly as FinalizeDrops writes them.
        var kill = Seed.AddKill(ctx, userId, charId, "REB_Sire", t, 1,
            [new("Rebuild Bludgeon claw", OverriddenItem, 1, 0), new("Rebuild Shark", PlainItem, 10, 1_000)]);
        await ctx.SaveChangesAsync();
        await SetProjectedPrice(ctx, kill.Id, OverriddenItem, OverrideValue);

        await Repo(ctx).RebuildDropsForCharacter(charId);

        var rows = await ctx.LootDrops.AsNoTracking()
            .Where(d => d.LootRecordId == kill.Id).ToListAsync();

        // THE REGRESSION: this came back 0 — the raw price out of DropsJson.
        Assert.Equal(OverrideValue, rows.Single(r => r.ItemId == OverriddenItem).Price);

        // An item with no override still re-derives from the raw price, unchanged.
        Assert.Equal(1_000, rows.Single(r => r.ItemId == PlainItem).Price);
    }

    [Fact]
    public async Task Rebuilding_the_projection_keeps_an_injected_special()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "rebuild-spec");
        var t = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);

        // A giga drop: no value at all, and the flag is the only thing marking it.
        var kill = Seed.AddKill(ctx, userId, charId, "REB_Inferno", t, 1,
            [new("Rebuild Infernal cape", SpecialItem, 1, 0, IsFirstTime: false, IsSpecial: true)]);
        await ctx.SaveChangesAsync();
        await SetProjectedSpecial(ctx, kill.Id, SpecialItem);

        await Repo(ctx).RebuildDropsForCharacter(charId);

        var row = await ctx.LootDrops.AsNoTracking().SingleAsync(d => d.LootRecordId == kill.Id);

        // THE REGRESSION: the rebuild's insert never listed IsSpecial, so it defaulted to false and
        // the drop fell out of the legendary lane, which finds specials by querying this column.
        Assert.True(row.IsSpecial);
    }

    [Fact]
    public async Task Recomputing_first_time_flags_does_not_reset_an_override()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "rebuild-reflag");
        var t = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);

        await AddOverride(ctx, ReflagItem, "Rebuild Abyssal head", OverrideValue);

        var first = Seed.AddKill(ctx, userId, charId, "REB_Unsired", t, 1,
            [new("Rebuild Abyssal head", ReflagItem, 1, 0)]);
        var second = Seed.AddKill(ctx, userId, charId, "REB_Unsired", t.AddMinutes(10), 2,
            [new("Rebuild Abyssal head", ReflagItem, 1, 0)]);
        await ctx.SaveChangesAsync();
        await SetProjectedPrice(ctx, first.Id, ReflagItem, OverrideValue);
        await SetProjectedPrice(ctx, second.Id, ReflagItem, OverrideValue);

        // The real trigger: this is what every imported batch runs, and what
        // SpecialLootHandler runs straight after injecting a drop.
        await Repo(ctx).RecomputeFirstTimeFlags(charId);

        var prices = await ctx.LootDrops.AsNoTracking()
            .Where(d => d.ItemId == ReflagItem).Select(d => d.Price).ToListAsync();

        Assert.Equal([OverrideValue, OverrideValue], prices);

        // And the reflag still did its own job: the earlier receipt is the first-time one.
        var flags = await ctx.LootDrops.AsNoTracking()
            .Where(d => d.ItemId == ReflagItem)
            .OrderBy(d => d.LootRecordId)
            .Select(d => d.IsFirstTime)
            .ToListAsync();
        Assert.Equal([true, false], flags);
    }

    private static async Task AddOverride(DataContext ctx, int itemId, string name, int value)
    {
        ctx.ItemValueOverrides.Add(new ItemValueOverride { ItemId = itemId, ItemName = name, Value = value });
        await ctx.SaveChangesAsync();
    }

    // Seed.AddKill projects the RAW price, because that is all the caller hands it. Ingest would
    // have written the effective one, so put the row into the state the rebuild has to preserve.
    private static async Task SetProjectedPrice(DataContext ctx, int recordId, int itemId, int price) =>
        await ctx.Database.ExecuteSqlRawAsync(
            """UPDATE "LootDrops" SET "Price" = {0} WHERE "LootRecordId" = {1} AND "ItemId" = {2}""",
            price, recordId, itemId);

    private static async Task SetProjectedSpecial(DataContext ctx, int recordId, int itemId) =>
        await ctx.Database.ExecuteSqlRawAsync(
            """UPDATE "LootDrops" SET "IsSpecial" = true WHERE "LootRecordId" = {0} AND "ItemId" = {1}""",
            recordId, itemId);
}
