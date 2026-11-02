using KlavLor.Application.Features.Loot.Summary;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Shared;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// The monthly summary against real SQL. What these pin would fail quietly in production: the
// primary character chosen by kills instead of time, a kill at 23:30 UTC filed under the wrong month,
// the "N new slots" headline disagreeing with the list beneath it, and — the one that matters most —
// a summary showing a character that belongs to somebody else.
//
// The fixture database is SHARED across the collection, so every assertion is scoped to users and
// characters this file seeded, and collection-log item ids sit in a range nothing else uses.
[Collection("postgres")]
public sealed class MonthlySummaryTests(PostgresFixture fx)
{
    private static readonly SummaryMonth October = new(2026, 10);
    private static readonly SummaryMonth September = new(2026, 9);

    // Mid-October, safely clear of either boundary.
    private static readonly DateTimeOffset Oct = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static LootMonthlySummaryRepository Repo(DataContext ctx) =>
        new(ctx, NullLogger<LootMonthlySummaryRepository>.Instance);

    private static LootProfileRepository ProfileRepo(DataContext ctx) =>
        new(ctx, NullLogger<LootProfileRepository>.Instance, Fakes.DropReader());

    private static MonthlySummaryHandler Handler(DataContext ctx, int? userId, params int[] petIds) =>
        new(Repo(ctx), ProfileRepo(ctx), new FakeCurrentUser(userId), new FakePetCache(petIds),
            new MemoryCache(new MemoryCacheOptions()));

    private static MonthlySummaryHandler AdminHandler(DataContext ctx, int adminId) =>
        new(Repo(ctx), ProfileRepo(ctx), new FakeCurrentUser(adminId, isAdmin: true), new FakePetCache([]),
            new MemoryCache(new MemoryCacheOptions()));

    private static LootDrop Coins(int amount) => new("Coins", 995, amount, 1);

    [Fact]
    public async Task The_primary_character_is_the_one_played_for_the_most_hours_not_the_most_kills()
    {
        await using var ctx = fx.CreateContext();
        var (userId, main) = await Seed.UserAndCharacter(ctx, "sum-primary");
        var alt = await AddCharacter(ctx, userId, "sum-alt");
        var idle = await AddCharacter(ctx, userId, "sum-idle");
        var (otherUser, otherChar) = await Seed.UserAndCharacter(ctx, "sum-other");

        // Main: ten kills, one in each of ten different evenings.
        for (var i = 0; i < 10; i++)
            Seed.AddKill(ctx, userId, main, "Chambers of Xeric", Oct.AddDays(i).AddHours(8), null, [Coins(1_000)]);
        // Alt: two hundred kills inside a single hour. More kills, far less time.
        for (var i = 0; i < 200; i++)
            Seed.AddKill(ctx, userId, alt, "Lizardman shaman", Oct.AddSeconds(i * 10), null, [Coins(100)]);
        // A September kill, which sets where the user's history begins.
        var first = new DateTimeOffset(2026, 9, 12, 18, 0, 0, TimeSpan.Zero);
        Seed.AddKill(ctx, userId, main, "Chambers of Xeric", first, null, [Coins(1_000)]);
        // Somebody else, busier than anyone, in the same month.
        for (var i = 0; i < 50; i++)
            Seed.AddKill(ctx, otherUser, otherChar, "Vorkath", Oct.AddHours(i), null, [Coins(50_000)]);
        await ctx.SaveChangesAsync();

        var activity = await Repo(ctx).GetActivity(userId, October.StartUtc, October.EndUtc);

        Assert.Equal([main, alt, idle], activity.Characters.Select(c => c.CharacterId));
        Assert.Equal(main, activity.Primary!.CharacterId);

        var m = activity.Characters[0];
        Assert.Equal(10, m.Kills);
        Assert.Equal(10, m.HourBuckets);
        Assert.Equal(10, m.ActiveDays);
        Assert.Equal(10_000, m.Gp);

        var a = activity.Characters[1];
        Assert.Equal(200, a.Kills);
        Assert.Equal(1, a.HourBuckets);

        Assert.False(activity.Characters[2].WasActive);
        Assert.DoesNotContain(activity.Characters, c => c.CharacterId == otherChar);
        Assert.Equal(first, activity.FirstActivityAt);
    }

    [Fact]
    public async Task A_month_is_bounded_by_London_midnight_not_UTC_midnight()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sum-tz");

        // 30 September, 22:30 UTC = 23:30 BST on the 30th: September.
        Seed.AddKill(ctx, userId, charId, "Zulrah", new DateTimeOffset(2026, 9, 30, 22, 30, 0, TimeSpan.Zero), null, [Coins(10)]);
        // 30 September, 23:30 UTC = 00:30 BST on 1 October: October.
        Seed.AddKill(ctx, userId, charId, "Zulrah", new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero), null, [Coins(10)]);
        // 31 October, 23:30 UTC is still 23:30 GMT on the 31st (the clocks went back on the 25th).
        Seed.AddKill(ctx, userId, charId, "Zulrah", new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero), null, [Coins(10)]);
        await ctx.SaveChangesAsync();

        var repo = Repo(ctx);
        Assert.Equal(1, (await repo.GetActivity(userId, September.StartUtc, September.EndUtc)).Primary!.Kills);
        Assert.Equal(2, (await repo.GetActivity(userId, October.StartUtc, October.EndUtc)).Primary!.Kills);
    }

    [Fact]
    public async Task The_new_slot_list_agrees_with_the_calendar_count_including_a_name_matched_item()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sum-clog");
        const string source = "Sum Clog Boss";
        Seed.AddClogItem(ctx, 945_001, "Sum Clog Fang", source);
        Seed.AddClogItem(ctx, 945_002, "Sum Clog Pet", source);
        Seed.AddClogItem(ctx, 945_003, "Sum Clog Visage", source);
        ctx.CharacterSourceBaselines.Add(new CharacterSourceBaseline
        {
            GameCharacterId = charId, SourceName = source, BaselineKc = 100
        });

        // September: a first-time clog item, outside the month.
        Seed.AddKill(ctx, userId, charId, source, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero), null,
            [new("Sum Clog Visage", 945_003, 1, 0, IsFirstTime: true)]);
        // October, kill #2 here: a first-time JUNK item, which is not a collection-log slot.
        Seed.AddKill(ctx, userId, charId, source, Oct, null,
            [new("Bones", 526, 1, 100, IsFirstTime: true)]);
        // Kill #3: matched by ITEM ID.
        Seed.AddKill(ctx, userId, charId, source, Oct.AddHours(1), 4_321,
            [new("Sum Clog Fang", 945_001, 1, 2_000_000, IsFirstTime: true)]);
        // Kill #4: matched by NAME only — RuneLite logged no usable id, and the case differs.
        Seed.AddKill(ctx, userId, charId, source, Oct.AddHours(2), null,
            [new("sum clog pet", 0, 1, 0, IsFirstTime: true)]);
        // Kill #5: a repeat of an item already owned, which is not a new slot.
        Seed.AddKill(ctx, userId, charId, source, Oct.AddHours(3), null,
            [new("Sum Clog Fang", 945_001, 1, 2_000_000)]);
        await ctx.SaveChangesAsync();

        var page = await Repo(ctx).GetNewCollectionLogItems(charId, October.StartUtc, October.EndUtc, limit: 50);
        var calendar = await ProfileRepo(ctx).GetActivityCalendar(charId, October.StartUtc, October.EndUtc);

        Assert.Equal(2, page.Total);
        Assert.Equal(page.Total, calendar.Sum(d => d.Clogs));
        Assert.Equal(["Sum Clog Fang", "sum clog pet"], page.Entries.Select(e => e.ItemName));

        var fang = page.Entries[0];
        Assert.Equal(4_321, fang.KillCount);
        // Its third kill at the source, plus the 100-kill baseline: the site's one roll-number rule.
        Assert.Equal(103, fang.KillOrdinal);
        Assert.Equal(2_000_000, fang.Value);
    }

    [Fact]
    public async Task The_slot_list_is_capped_but_reports_the_full_total()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sum-cap");
        for (var i = 0; i < 4; i++)
        {
            Seed.AddClogItem(ctx, 945_100 + i, $"Sum Cap Item {i}");
            Seed.AddKill(ctx, userId, charId, "Sum Cap Boss", Oct.AddHours(i), null,
                [new($"Sum Cap Item {i}", 945_100 + i, 1, 0, IsFirstTime: true)]);
        }
        await ctx.SaveChangesAsync();

        var page = await Repo(ctx).GetNewCollectionLogItems(charId, October.StartUtc, October.EndUtc, limit: 3);

        Assert.Equal(4, page.Total);
        Assert.Equal(["Sum Cap Item 0", "Sum Cap Item 1", "Sum Cap Item 2"], page.Entries.Select(e => e.ItemName));
    }

    [Fact]
    public async Task Sources_report_kill_count_at_the_start_and_end_of_the_month()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sum-src");

        // Vorkath: 5 kills before October, 3 in it, a 44-kill baseline. 49 → 52 crosses 50.
        for (var i = 0; i < 5; i++)
            Seed.AddKill(ctx, userId, charId, "Sum Vorkath", Oct.AddMonths(-1).AddHours(i), null, [Coins(10)]);
        for (var i = 0; i < 3; i++)
            Seed.AddKill(ctx, userId, charId, "Sum Vorkath", Oct.AddHours(i), null, [Coins(100_000)]);
        ctx.CharacterSourceBaselines.Add(new CharacterSourceBaseline
        {
            GameCharacterId = charId, SourceName = "Sum Vorkath", BaselineKc = 44
        });
        // Zulrah: two kills, RuneLite reporting the in-game count — far above what we tracked.
        Seed.AddKill(ctx, userId, charId, "Sum Zulrah", Oct.AddDays(1), 999, [Coins(10)]);
        Seed.AddKill(ctx, userId, charId, "Sum Zulrah", Oct.AddDays(1).AddMinutes(2), 1_000, [Coins(10)]);
        // A source never killed before.
        Seed.AddKill(ctx, userId, charId, "Sum Scurrius", Oct.AddDays(2), null, [Coins(10)]);
        // A November kill, which must not leak into October's end count.
        Seed.AddKill(ctx, userId, charId, "Sum Vorkath", new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero), null, [Coins(10)]);
        await ctx.SaveChangesAsync();

        var page = await Repo(ctx).GetSources(charId, October.StartUtc, October.EndUtc, limit: 10);

        Assert.Equal(3, page.Total);
        Assert.Equal(["Sum Vorkath", "Sum Zulrah", "Sum Scurrius"], page.Rows.Select(r => r.SourceName));

        var vorkath = page.Rows[0];
        Assert.Equal(3, vorkath.Kills);
        Assert.Equal(300_000, vorkath.Gp);
        Assert.Equal(49, vorkath.StartKc);
        Assert.Equal(52, vorkath.EndKc);
        Assert.Equal(50, vorkath.Milestone);

        var zulrah = page.Rows[1];
        Assert.Equal(998, zulrah.StartKc);
        Assert.Equal(1_000, zulrah.EndKc);
        Assert.Equal(1_000, zulrah.Milestone);

        Assert.True(page.Rows[2].IsFirstEver);

        var capped = await Repo(ctx).GetSources(charId, October.StartUtc, October.EndUtc, limit: 2);
        Assert.Equal(2, capped.Rows.Count);
        Assert.Equal(3, capped.Total);
    }

    [Fact]
    public async Task Notable_drops_keep_each_items_best_receipt_above_the_floor()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sum-drops");

        Seed.AddKill(ctx, userId, charId, "Sum Drop Boss", Oct, null,
            [new("Sum Fang", 945_201, 1, 5_000_000), new("Sum Bones", 945_202, 1, 500)]);
        Seed.AddKill(ctx, userId, charId, "Sum Drop Boss", Oct.AddDays(3), null,
            [new("Sum Fang", 945_201, 1, 8_000_000)]);
        Seed.AddKill(ctx, userId, charId, "Sum Other Boss", Oct.AddDays(4), null,
            [new("Sum Visage", 945_203, 1, 3_000_000)]);
        // Value is quantity times price: a 20k stack of a 1gp item clears a 10k floor.
        Seed.AddKill(ctx, userId, charId, "Sum Other Boss", Oct.AddDays(5), null,
            [new("Sum Coins", 945_204, 20_000, 1)]);
        // Just under the floor.
        Seed.AddKill(ctx, userId, charId, "Sum Other Boss", Oct.AddDays(6), null,
            [new("Sum Junk", 945_205, 1, 9_999)]);
        // Worth a fortune, but in September.
        Seed.AddKill(ctx, userId, charId, "Sum Drop Boss", Oct.AddMonths(-1), null,
            [new("Sum Bow", 945_206, 1, 1_000_000_000)]);
        await ctx.SaveChangesAsync();

        var drops = await Repo(ctx).GetNotableDrops(charId, October.StartUtc, October.EndUtc, floor: 10_000, limit: 10);

        Assert.Equal(["Sum Fang", "Sum Visage", "Sum Coins"], drops.Select(d => d.ItemName));
        Assert.Equal(8_000_000, drops[0].Value);
        Assert.Equal(Oct.AddDays(3), drops[0].OccurredAt);
        Assert.Equal(20_000, drops[2].Quantity);
    }

    [Fact]
    public async Task A_summary_never_shows_a_character_belonging_to_someone_else()
    {
        await using var ctx = fx.CreateContext();
        var (userA, charA) = await Seed.UserAndCharacter(ctx, "sum-own-a");
        var idleA = await AddCharacter(ctx, userA, "sum-own-idle");
        var (userB, charB) = await Seed.UserAndCharacter(ctx, "sum-own-b");
        Seed.AddKill(ctx, userA, charA, "Sum Own Boss A", Oct, null, [Coins(20_000)]);
        Seed.AddKill(ctx, userB, charB, "Sum Own Boss B", Oct, null, [Coins(20_000)]);
        await ctx.SaveChangesAsync();

        var handler = Handler(ctx, userA);

        // B's character, asked for by id: ignored, and A's own primary shown instead.
        var stolen = (await handler.Overview(October.Slug, charB)).Value!.Context;
        Assert.Equal(charA, stolen.Character!.CharacterId);
        Assert.False(stolen.CharacterRequested);
        Assert.True(stolen.IsPrimary);

        var sources = (await handler.Sources(October.Slug, charB)).Value!;
        Assert.Equal(["Sum Own Boss A"], sources.Page.Rows.Select(r => r.SourceName));

        // A's OWN character is honoured even though it did nothing that month.
        var picked = (await handler.Overview(October.Slug, idleA)).Value!.Context;
        Assert.Equal(idleA, picked.Character!.CharacterId);
        Assert.True(picked.CharacterRequested);
        Assert.False(picked.IsPrimary);
        Assert.False(picked.Character.WasActive);
    }

    [Fact]
    public async Task A_month_with_no_play_has_an_overview_and_no_other_panels()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sum-empty");
        Seed.AddKill(ctx, userId, charId, "Sum Empty Boss", Oct.AddMonths(-1), null, [Coins(10)]);
        await ctx.SaveChangesAsync();

        var handler = Handler(ctx, userId);

        var overview = await handler.Overview(October.Slug, null);
        Assert.True(overview.IsSuccess);
        Assert.Null(overview.Value!.Context.Character);
        Assert.Empty(overview.Value.Context.ActiveCharacters);
        Assert.True(overview.Value.Context.HasPrevious);

        var collection = await handler.Collection(October.Slug, null);
        Assert.True(collection.IsSuccess);
        Assert.Null(collection.Value);
    }

    [Fact]
    public async Task The_overview_compares_against_the_same_character_last_month()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sum-prev");
        for (var i = 0; i < 4; i++)
            Seed.AddKill(ctx, userId, charId, "Sum Prev Boss", Oct.AddMonths(-1).AddDays(i), null, [Coins(10)]);
        for (var i = 0; i < 6; i++)
            Seed.AddKill(ctx, userId, charId, "Sum Prev Boss", Oct.AddDays(i), null, [Coins(10)]);
        await ctx.SaveChangesAsync();

        var overview = (await Handler(ctx, userId).Overview(October.Slug, null)).Value!;

        Assert.Equal(6, overview.Context.Character!.Kills);
        Assert.Equal(4, overview.PreviousMonth!.Kills);
        Assert.Equal(6, overview.Days.Count);
        Assert.Equal(6, overview.LongestStreak);
    }

    [Fact]
    public async Task A_pet_in_the_new_slots_is_marked_as_one()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sum-pet");
        Seed.AddClogItem(ctx, 945_301, "Sum Olmlet");
        Seed.AddClogItem(ctx, 945_302, "Sum Dust");
        Seed.AddKill(ctx, userId, charId, "Sum Raid", Oct, null,
            [new("Sum Olmlet", 945_301, 1, 0, IsFirstTime: true), new("Sum Dust", 945_302, 1, 0, IsFirstTime: true)]);
        await ctx.SaveChangesAsync();

        var collection = (await Handler(ctx, userId, petIds: 945_301).Collection(October.Slug, null)).Value!;

        Assert.True(collection.Page.Entries.Single(e => e.ItemName == "Sum Olmlet").IsPet);
        Assert.False(collection.Page.Entries.Single(e => e.ItemName == "Sum Dust").IsPet);
    }

    [Fact]
    public async Task An_admin_sees_another_players_summary_exactly_as_that_player_does()
    {
        await using var ctx = fx.CreateContext();
        var (adminId, adminChar) = await Seed.UserAndCharacter(ctx, "sum-admin");
        var (playerId, main) = await Seed.UserAndCharacter(ctx, "sum-player");
        var alt = await AddCharacter(ctx, playerId, "sum-player-alt");
        Seed.AddKill(ctx, adminId, adminChar, "Sum Admin Boss", Oct, null, [Coins(20_000)]);
        for (var i = 0; i < 3; i++)
            Seed.AddKill(ctx, playerId, main, "Sum Player Boss", Oct.AddDays(i), null, [Coins(20_000)]);
        Seed.AddKill(ctx, playerId, alt, "Sum Player Alt Boss", Oct, null, [Coins(20_000)]);
        Seed.AddKill(ctx, playerId, main, "Sum Player Boss", Oct.AddMonths(-1), null, [Coins(20_000)]);
        await ctx.SaveChangesAsync();

        var admin = AdminHandler(ctx, adminId);
        var overview = (await admin.Overview(October.Slug, alt)).Value!;
        var context = overview.Context;

        Assert.Equal(alt, context.Character!.CharacterId);
        Assert.Equal("Test sum-player", context.AdminViewOf);
        Assert.True(context.CharacterRequested);
        // Built from the PLAYER's activity: their two characters, their primary, their history.
        Assert.Equal([main, alt], context.ActiveCharacters.Select(c => c.CharacterId));
        Assert.False(context.IsPrimary);
        Assert.Equal(September, context.EarliestMonth);
        Assert.True((await admin.Overview(October.Slug, main)).Value!.Context.IsPrimary);
        Assert.NotNull((await admin.Overview(October.Slug, main)).Value!.PreviousMonth);

        var sources = (await admin.Sources(October.Slug, alt)).Value!;
        Assert.Equal(["Sum Player Alt Boss"], sources.Page.Rows.Select(r => r.SourceName));

        // The admin's OWN summary is unaffected, and not marked as an admin view.
        var own = (await admin.Overview(October.Slug, null)).Value!.Context;
        Assert.Equal(adminChar, own.Character!.CharacterId);
        Assert.Null(own.AdminViewOf);

        // And the same request from the player's peer, who is not an admin, still falls back.
        var (peerId, peerChar) = await Seed.UserAndCharacter(ctx, "sum-peer");
        Seed.AddKill(ctx, peerId, peerChar, "Sum Peer Boss", Oct, null, [Coins(20_000)]);
        await ctx.SaveChangesAsync();
        var peer = (await Handler(ctx, peerId).Overview(October.Slug, alt)).Value!.Context;
        Assert.Equal(peerChar, peer.Character!.CharacterId);
        Assert.Null(peer.AdminViewOf);
    }

    [Fact]
    public async Task The_admin_roster_lists_every_character_most_active_first_and_is_admin_only()
    {
        await using var ctx = fx.CreateContext();
        var (adminId, _) = await Seed.UserAndCharacter(ctx, "sum-roster-admin");
        var (busyId, busy) = await Seed.UserAndCharacter(ctx, "sum-roster-busy");
        var (quietId, quiet) = await Seed.UserAndCharacter(ctx, "sum-roster-quiet", leagues: true);
        var (_, idle) = await Seed.UserAndCharacter(ctx, "sum-roster-idle");
        for (var i = 0; i < 5; i++)
            Seed.AddKill(ctx, busyId, busy, "Sum Roster Boss", Oct.AddHours(i * 3), null, [Coins(20_000)]);
        Seed.AddKill(ctx, quietId, quiet, "Sum Roster Boss", Oct, null, [Coins(90_000)]);
        await ctx.SaveChangesAsync();

        var roster = (await AdminHandler(ctx, adminId).Roster(October.Slug)).Value!;
        var ours = roster.Characters
            .Where(e => e.Character.CharacterId == busy || e.Character.CharacterId == quiet || e.Character.CharacterId == idle)
            .ToList();

        Assert.Equal(October, roster.Month);
        Assert.Equal([busy, quiet, idle], ours.Select(e => e.Character.CharacterId));
        Assert.Equal("Test sum-roster-busy", ours[0].OwnerName);
        Assert.Equal(5, ours[0].Character.Kills);
        Assert.True(ours[1].IsLeagues);
        Assert.False(ours[2].Character.WasActive);
        Assert.Contains(roster.Idle, e => e.Character.CharacterId == idle);
        Assert.DoesNotContain(roster.Played, e => e.Character.CharacterId == idle);

        Assert.False((await Handler(ctx, busyId).Roster(October.Slug)).IsSuccess);
    }

    private static async Task<int> AddCharacter(DataContext ctx, int userId, string tag)
    {
        var character = new GameCharacter
        {
            UserId = userId,
            RuneLiteId = $"{tag}-{Guid.NewGuid():N}",
            DisplayName = $"{tag}-{Guid.NewGuid():N}"[..20],
            IsVisible = true
        };
        ctx.GameCharacters.Add(character);
        await ctx.SaveChangesAsync();
        return character.Id;
    }

    private sealed class FakeCurrentUser(int? userId, bool isAdmin = false) : ICurrentUser
    {
        public int? UserId => userId;
        public bool IsAdmin => isAdmin;
        public bool IsInRole(RoleName roleName) =>
            roleName == RoleName.User || (isAdmin && roleName == RoleName.Admin);
    }
}
