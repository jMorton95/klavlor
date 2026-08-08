using KlavLor.Application.Interfaces.Services;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.EntityFrameworkCore;

namespace KlavLor.IntegrationTests;

// The re-derivation pass behind the admin "item values" panel. Setting an intrinsic value has to
// rewrite the stored LootDrops projection AND roll the affected LootRecords totals back up, or the
// SQL surfaces (monthly graphs, daily heatmap, source totals, top items) would keep reporting the
// old figure while the in-memory feed path reported the new one.
//
// Removal has to be exactly symmetric: DropsJson is never rewritten, so the raw RuneLite price is
// still there to re-derive from. These tests pin both directions against real SQL.
[Collection("postgres")]
public sealed class ItemValueOverrideRebuildTests(PostgresFixture fx)
{
    // The Postgres container is shared across the whole collection, so every test owns a distinct
    // item id — a rebuild is keyed on item id and would otherwise sweep up another test's rows.
    private const int NoxPointSet = 90_011;   // untradeable component, RuneLite prices it at 0
    private const int SharkSet = 90_012;      // ordinary priced drop sharing the same kill
    private const int NoxPointRemove = 90_021;
    private const int NoxPointIdempotent = 90_031;

    [Fact]
    public async Task Setting_a_value_reprices_stored_drops_and_recomputes_record_totals()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "ivo-set");
        var t = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

        // Two kills with the untradeable, one without — the third must be left alone entirely.
        Seed.AddKill(ctx, userId, charId, "IVO_Araxxor", t, 1,
            [new("IVO Noxious point", NoxPointSet, 1, 0), new("IVO Shark", SharkSet, 10, 1_000)]);
        Seed.AddKill(ctx, userId, charId, "IVO_Araxxor", t.AddMinutes(5), 2,
            [new("IVO Noxious point", NoxPointSet, 2, 0)]);
        var untouched = Seed.AddKill(ctx, userId, charId, "IVO_Araxxor", t.AddMinutes(10), 3,
            [new("IVO Shark", SharkSet, 5, 1_000)]);
        await ctx.SaveChangesAsync();
        var untouchedId = untouched.Id;

        var cache = new FakeItemValueCache((NoxPointSet, 10_000_000));
        var repo = new ItemValueOverrideRepository(ctx, cache);

        var result = await repo.RebuildForItem(NoxPointSet);

        Assert.Equal(2, result.RecordsUpdated);
        Assert.Equal([charId], result.CharacterIds);
        Assert.Equal(["IVO_Araxxor"], result.SourceNames);

        // The projection now carries the effective price...
        var rows = await ctx.LootDrops.AsNoTracking()
            .Where(d => d.ItemId == NoxPointSet).OrderBy(d => d.Id).ToListAsync();
        Assert.Equal([10_000_000, 10_000_000], rows.Select(r => r.Price));

        // ...the other item in the same kill is untouched...
        var sharkRows = await ctx.LootDrops.AsNoTracking().Where(d => d.ItemId == SharkSet).ToListAsync();
        Assert.All(sharkRows, r => Assert.Equal(1_000, r.Price));

        // ...and the record totals were rolled back up: 10m + (10 × 1k), then 2 × 10m.
        var totals = await ctx.LootRecords.AsNoTracking()
            .Where(r => r.GameCharacterId == charId)
            .OrderBy(r => r.OccurredAt)
            .Select(r => r.TotalValue)
            .ToListAsync();
        Assert.Equal([10_010_000L, 20_000_000L, 5_000L], totals);

        // The kill that never contained the item was not rewritten at all.
        Assert.Equal(5_000L, await ctx.LootRecords.AsNoTracking()
            .Where(r => r.Id == untouchedId).Select(r => r.TotalValue).FirstAsync());
    }

    [Fact]
    public async Task Removing_a_value_restores_the_raw_price_from_the_canonical_DropsJson()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "ivo-rm");
        var t = new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero);

        // A raw price of 4,200 rather than 0, so "restored" is provably the stored figure and not
        // just an incidental zero.
        Seed.AddKill(ctx, userId, charId, "IVO_Araxxor", t, 1,
            [new("IVO Noxious point", NoxPointRemove, 3, 4_200)]);
        await ctx.SaveChangesAsync();

        var cache = new FakeItemValueCache();
        var repo = new ItemValueOverrideRepository(ctx, cache);

        cache.Replace([new ItemValueOverrideValue(NoxPointRemove, "IVO Noxious point", 10_000_000)]);
        await repo.RebuildForItem(NoxPointRemove);
        Assert.Equal(30_000_000L, await ctx.LootRecords.AsNoTracking()
            .Where(r => r.GameCharacterId == charId).Select(r => r.TotalValue).FirstAsync());

        // Override removed → back to 3 × 4,200. DropsJson never changed, which is what makes this
        // reversible rather than a one-way rewrite.
        cache.Replace([]);
        await repo.RebuildForItem(NoxPointRemove);

        var row = await ctx.LootDrops.AsNoTracking().FirstAsync(d => d.ItemId == NoxPointRemove);
        Assert.Equal(4_200, row.Price);
        Assert.Equal(12_600L, await ctx.LootRecords.AsNoTracking()
            .Where(r => r.GameCharacterId == charId).Select(r => r.TotalValue).FirstAsync());
    }

    [Fact]
    public async Task A_rebuild_is_idempotent_and_reports_no_work_when_nothing_changes()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "ivo-idem");
        var t = new DateTimeOffset(2026, 3, 3, 12, 0, 0, TimeSpan.Zero);

        Seed.AddKill(ctx, userId, charId, "IVO_Araxxor", t, 1, [new("IVO Noxious point", NoxPointIdempotent, 1, 0)]);
        await ctx.SaveChangesAsync();

        var repo = new ItemValueOverrideRepository(ctx, new FakeItemValueCache((NoxPointIdempotent, 10_000_000)));

        Assert.Equal(1, (await repo.RebuildForItem(NoxPointIdempotent)).RecordsUpdated);
        // Second pass: everything already holds the effective price, so nothing is rewritten.
        Assert.Equal(0, (await repo.RebuildForItem(NoxPointIdempotent)).RecordsUpdated);
        Assert.Equal(10_000_000L, await ctx.LootRecords.AsNoTracking()
            .Where(r => r.GameCharacterId == charId).Select(r => r.TotalValue).FirstAsync());
    }

    [Fact]
    public async Task Rebuilding_an_item_that_was_never_dropped_is_a_no_op()
    {
        await using var ctx = fx.CreateContext();
        var repo = new ItemValueOverrideRepository(ctx, new FakeItemValueCache((999_999, 1_000)));

        var result = await repo.RebuildForItem(999_999);

        Assert.Equal(0, result.RecordsUpdated);
        Assert.Empty(result.CharacterIds);
        Assert.Empty(result.SourceNames);
    }
}
