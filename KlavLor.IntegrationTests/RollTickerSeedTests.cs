using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// The roll ticker's buffer is in memory, so without a startup seed the banner is blank after every
// restart until the clan's next kill. GetRecentRolls is that seed; these pin what it may and may
// not put on a banner labelled LIVE.
[Collection("postgres")]
public sealed class RollTickerSeedTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static LootFeedRepository Repo(KlavLor.Infrastructure.Persistence.EntityFramework.DataContext ctx)
        => new(ctx, NullLogger<LootFeedRepository>.Instance, new FakeClogCache(), new FakeItemValueCache());

    [Fact]
    public async Task The_seed_returns_the_newest_kills_first_with_no_loot()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "roll-seed");

        foreach (var i in Enumerable.Range(0, 5))
            Seed.AddKill(ctx, userId, charId, "RS_Vorkath", T.AddMinutes(i), 100 + i,
                [new("Bones", 1, 1, 100)]);
        await ctx.SaveChangesAsync();

        var rows = (await Repo(ctx).GetRecentRolls(LootFeedScope.Main, 40))
            .Where(r => r.GameCharacterId == charId)
            .ToList();

        Assert.Equal(5, rows.Count);

        // Newest first — the endpoint reverses this for oldest-first seeding, because the banner
        // prepends and the last one written must end up leftmost.
        Assert.Equal(T.AddMinutes(4), rows[0].OccurredAt);
        Assert.Equal(T, rows[^1].OccurredAt);

        // RuneLite's reported count rides along; the ordinal is resolved separately so this row
        // type deliberately has nowhere to put one.
        Assert.Equal(104, rows[0].KillCount);
        Assert.Equal("RS_Vorkath", rows[0].SourceName);
    }

    [Fact]
    public async Task Imported_history_never_seeds_the_ticker()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "roll-import");

        var live = Seed.AddKill(ctx, userId, charId, "RS_Live", T, 1, [new("Bones", 1, 1, 100)]);
        var imported = Seed.AddKill(ctx, userId, charId, "RS_Imported", T.AddMinutes(1), 1,
            [new("Bones", 1, 1, 100)]);
        imported.IsImported = true;
        await ctx.SaveChangesAsync();

        var rows = (await Repo(ctx).GetRecentRolls(LootFeedScope.Main, 40))
            .Where(r => r.GameCharacterId == charId)
            .ToList();

        // The imported one is NEWER, so it would lead the banner if it were not excluded — a first
        // sync with full history would fill a banner labelled LIVE with months-old kills.
        var row = Assert.Single(rows);
        Assert.Equal(live.Id, row.RecordId);
        Assert.Equal("RS_Live", row.SourceName);
    }

    [Theory]
    [InlineData(true, true, false)]    // Leagues character, main scope
    [InlineData(false, true, true)]    // admin-hidden
    [InlineData(false, false, false)]  // not visible
    public async Task A_character_outside_the_scopes_roster_never_seeds_it(
        bool leagues, bool visible, bool hidden)
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(
            ctx, "roll-vis", leagues: leagues, visible: visible, hidden: hidden);

        Seed.AddKill(ctx, userId, charId, "RS_Hidden", T, 1, [new("Bones", 1, 1, 100)]);
        await ctx.SaveChangesAsync();

        var rows = await Repo(ctx).GetRecentRolls(LootFeedScope.Main, 40);
        Assert.DoesNotContain(rows, r => r.GameCharacterId == charId);
    }

    [Fact]
    public async Task Leagues_kills_seed_the_leagues_ticker_and_not_the_main_one()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "roll-lg", leagues: true);

        Seed.AddKill(ctx, userId, charId, "RS_Leagues", T, 1, [new("Bones", 1, 1, 100)]);
        await ctx.SaveChangesAsync();

        var repo = Repo(ctx);
        Assert.Contains(await repo.GetRecentRolls(LootFeedScope.Leagues, 40), r => r.GameCharacterId == charId);
        Assert.DoesNotContain(await repo.GetRecentRolls(LootFeedScope.Main, 40), r => r.GameCharacterId == charId);
    }

    [Fact]
    public void Seeding_the_buffer_replaces_it_and_hands_back_newest_first()
    {
        var service = new KlavLor.Infrastructure.Services.LootRollFeedService();

        // Seeded oldest-first, the order the seeder writes in.
        service.SeedBuffer(LootFeedScope.Main,
        [
            new LootRollEntry("A", 1, "Vorkath", 1, T),
            new LootRollEntry("A", 1, "Vorkath", 2, T.AddMinutes(1))
        ]);

        var recent = service.GetRecent(LootFeedScope.Main);
        Assert.Equal([2, 1], recent.Select(r => r.KillOrdinal).ToArray());

        // A second seed REPLACES rather than appends, so a re-run cannot double the banner.
        service.SeedBuffer(LootFeedScope.Main, [new LootRollEntry("B", 2, "Zulrah", 9, T.AddMinutes(2))]);
        var reseeded = service.GetRecent(LootFeedScope.Main);
        Assert.Equal([9], reseeded.Select(r => r.KillOrdinal).ToArray());

        // Scopes are independent.
        Assert.Empty(service.GetRecent(LootFeedScope.Leagues));
    }
}
