using KlavLor.Application.Features.Loot.Superiors;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// The Superior Slayer comparison against real SQL. These pin the properties that would fail
// silently in production: case-insensitive source matching (RuneLite, the wiki article titles and
// the wiki's summary table disagree on case for a third of the list), the visibility filter, the
// baseline arithmetic, and the base-monster count.
//
// The fixture database is SHARED across this collection, so the repository assertions scope
// themselves to characters this file seeded, and the handler assertions — which necessarily see the
// whole roster — assert properties of the result rather than an exact row list.
[Collection("postgres")]
public sealed class SuperiorSlayerComparisonTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static SuperiorSlayerRepository Repo(KlavLor.Infrastructure.Persistence.EntityFramework.DataContext ctx)
        => new(ctx, NullLogger<SuperiorSlayerRepository>.Instance);

    private static Task<List<SuperiorCountRow>> Counts(SuperiorSlayerRepository repo)
        => repo.GetCounts(SuperiorSlayerMonsters.LoweredNames);

    [Fact]
    public async Task Counts_are_per_character_per_monster_with_first_and_last_dates()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-counts");

        Seed.AddKill(ctx, userId, charId, "Greater abyssal demon", T, null, [new("Coins", 995, 500, 1)]);
        Seed.AddKill(ctx, userId, charId, "Greater abyssal demon", T.AddDays(3), null, [new("Coins", 995, 700, 1)]);
        Seed.AddKill(ctx, userId, charId, "Greater abyssal demon", T.AddDays(9), null, [new("Coins", 995, 200, 1)]);
        Seed.AddKill(ctx, userId, charId, "Colossal Hydra", T.AddDays(5), null, [new("Coins", 995, 900, 1)]);
        // Not a superior — the BASE monster must never be counted as one.
        Seed.AddKill(ctx, userId, charId, "Abyssal demon", T.AddDays(1), null, [new("Coins", 995, 50, 1)]);
        await ctx.SaveChangesAsync();

        var rows = (await Counts(Repo(ctx))).Where(r => r.GameCharacterId == charId).ToList();

        Assert.Equal(2, rows.Count);

        var abyssal = rows.Single(r => r.SourceKey == "greater abyssal demon");
        Assert.Equal(3, abyssal.Kills);
        Assert.Equal(T, abyssal.FirstKilled);
        Assert.Equal(T.AddDays(9), abyssal.LastKilled);

        Assert.Equal(1, rows.Single(r => r.SourceKey == "colossal hydra").Kills);
    }

    [Fact]
    public async Task A_monsters_kills_are_counted_whatever_case_they_were_stored_in()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-case");

        // Three spellings of one monster, all of which really occur: the wiki article title, the
        // wiki summary table's display text, and an all-caps stand-in for whatever a client sends.
        // They must land on ONE row — a case split would quietly halve every count on the page.
        Seed.AddKill(ctx, userId, charId, "Chasm Crawler", T, null, [new("Coins", 995, 10, 1)]);
        Seed.AddKill(ctx, userId, charId, "chasm crawler", T.AddHours(1), null, [new("Coins", 995, 10, 1)]);
        Seed.AddKill(ctx, userId, charId, "CHASM CRAWLER", T.AddHours(2), null, [new("Coins", 995, 10, 1)]);
        await ctx.SaveChangesAsync();

        var rows = (await Counts(Repo(ctx))).Where(r => r.GameCharacterId == charId).ToList();

        var row = Assert.Single(rows);
        Assert.Equal("chasm crawler", row.SourceKey);
        Assert.Equal(3, row.Kills);
        Assert.Equal("Chasm Crawler", SuperiorSlayerMonsters.Canonical(row.SourceKey));
    }

    [Theory]
    [InlineData(true, true, false)]    // Leagues: seasonal loot must not bleed into a main-game view
    [InlineData(false, true, true)]    // admin-hidden
    [InlineData(false, false, false)]  // not visible
    public async Task A_character_outside_the_public_roster_is_excluded(
        bool leagues, bool visible, bool hidden)
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(
            ctx, "sup-vis", leagues: leagues, visible: visible, hidden: hidden);

        Seed.AddKill(ctx, userId, charId, "Night beast", T, null, [new("Coins", 995, 10, 1)]);
        await ctx.SaveChangesAsync();

        Assert.DoesNotContain(await Counts(Repo(ctx)), r => r.GameCharacterId == charId);
    }

    [Fact]
    public async Task An_admin_baseline_is_added_to_the_tracked_count()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-base");

        Seed.AddKill(ctx, userId, charId, "Marble gargoyle", T, null, [new("Coins", 995, 10, 1)]);
        Seed.AddKill(ctx, userId, charId, "Marble gargoyle", T.AddHours(1), null, [new("Coins", 995, 10, 1)]);
        ctx.CharacterSourceBaselines.Add(new CharacterSourceBaseline
        {
            GameCharacterId = charId,
            SourceName = "Marble gargoyle",
            BaselineKc = 40
        });
        await ctx.SaveChangesAsync();

        var rows = (await Counts(Repo(ctx))).Where(r => r.GameCharacterId == charId).ToList();

        // Same definition as LootSourceDetailRepository.GetSourceCollection, so this page and the
        // character's own source page can never quote different numbers for the same monster.
        Assert.Equal(42, Assert.Single(rows).Kills);
    }

    [Fact]
    public async Task The_handler_shows_only_killed_monsters_hardest_first()
    {
        await using var ctx = fx.CreateContext();
        var (aUser, aChar) = await Seed.UserAndCharacter(ctx, "sup-hand-a");
        var (bUser, bChar) = await Seed.UserAndCharacter(ctx, "sup-hand-b");

        // A kills more Night beasts, B kills more Choke devils.
        foreach (var i in Enumerable.Range(0, 5))
            Seed.AddKill(ctx, aUser, aChar, "Night beast", T.AddHours(i), null, [new("Coins", 995, 10, 1)]);
        Seed.AddKill(ctx, bUser, bChar, "Night beast", T, null, [new("Coins", 995, 10, 1)]);
        foreach (var i in Enumerable.Range(0, 3))
            Seed.AddKill(ctx, bUser, bChar, "Choke devil", T.AddHours(i), null, [new("Coins", 995, 10, 1)]);

        // The BASE monster, both ways it can be counted: 20 tracked Dark beast kills for A, and a
        // 100 admin baseline for B, who has no Dark beast records at all.
        foreach (var i in Enumerable.Range(0, 20))
            Seed.AddKill(ctx, aUser, aChar, "Dark beast", T.AddMinutes(i), null, [new("Coins", 995, 10, 1)]);
        ctx.CharacterSourceBaselines.Add(new CharacterSourceBaseline
        {
            GameCharacterId = bChar,
            SourceName = "Dark beast",
            BaselineKc = 100
        });
        await ctx.SaveChangesAsync();

        var handler = new SuperiorSlayerHandler(Repo(ctx), new MemoryCache(new MemoryCacheOptions()));
        var comparison = await handler.Get();

        // The handler reads the whole roster and the fixture database is shared, so these assert
        // PROPERTIES of the result rather than an exact row list — other tests in this collection
        // contribute superiors of their own.

        // ONLY MONSTERS SOMEONE HAS KILLED: no row is present with nothing behind it, and a superior
        // no test touches is absent entirely rather than sitting there as a line of dashes.
        Assert.All(comparison.Rows, r => Assert.True(r.TotalKills > 0, $"{r.Name} has no kills"));
        Assert.DoesNotContain(comparison.Rows, r => r.Name == "Mutated Tortoise");
        Assert.Contains(comparison.Rows, r => r.Name == "Night beast");
        Assert.Contains(comparison.Rows, r => r.Name == "Choke devil");

        // HIGHEST SLAYER LEVEL FIRST: the registry is stored ascending, the page reads hardest-first
        // because those are the rows that roll the shared unique table most often. The reversal is
        // the handler's, so it is pinned here rather than in the registry's own ordering test.
        var levels = comparison.Rows.Select(r => r.SlayerLevel).ToList();
        Assert.Equal(levels.OrderByDescending(l => l).ToList(), levels);
        Assert.True(
            comparison.Rows.Select(r => r.Name).ToList().IndexOf("Night beast")
            < comparison.Rows.Select(r => r.Name).ToList().IndexOf("Choke devil"),
            "Night beast (90) must sort above Choke devil (65)");

        var nightBeast = comparison.Rows.Single(r => r.Name == "Night beast");
        Assert.Equal(5, nightBeast.KillsFor(aChar));
        Assert.Equal(1, nightBeast.KillsFor(bChar));

        var chokeDevil = comparison.Rows.Single(r => r.Name == "Choke devil");
        Assert.Equal(0, chokeDevil.KillsFor(aChar));
        Assert.Equal(3, chokeDevil.KillsFor(bChar));

        // Base-monster kills are PER PLAYER, and both routes into them are covered: A's 20 come from
        // tracked records, B's 100 from an admin baseline with no records at all. Rolling them into
        // one roster figure would lose exactly the comparison the column exists to make.
        Assert.Equal(20, nightBeast.BaseKillsFor(aChar));
        Assert.Equal(100, nightBeast.BaseKillsFor(bChar));

        // Dust devil was never recorded for either of them.
        Assert.Equal(0, chokeDevil.BaseKillsFor(aChar));
        Assert.Equal(0, chokeDevil.BaseKillsFor(bChar));

        var a = comparison.Characters.Single(c => c.GameCharacterId == aChar);
        var b = comparison.Characters.Single(c => c.GameCharacterId == bChar);
        Assert.Equal(5, a.TotalKills);
        Assert.Equal(4, b.TotalKills);

        // Columns are ordered by total superior kills descending.
        var ordered = comparison.Characters.Select(c => c.GameCharacterId).ToList();
        Assert.True(ordered.IndexOf(aChar) < ordered.IndexOf(bChar));
    }

    [Fact]
    public async Task Sorting_by_a_character_ranks_the_rows_by_their_counts()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-sort");

        // Deliberately inverse to Slayer level: the level-95 Colossal Hydra gets the FEWEST kills,
        // so a character sort has to visibly disagree with the default ordering for this to pass.
        Seed.AddKill(ctx, userId, charId, "Colossal Hydra", T, null, [new("Coins", 995, 10, 1)]);
        foreach (var i in Enumerable.Range(0, 4))
            Seed.AddKill(ctx, userId, charId, "Choke devil", T.AddHours(i), null, [new("Coins", 995, 10, 1)]);
        foreach (var i in Enumerable.Range(0, 9))
            Seed.AddKill(ctx, userId, charId, "Crushing hand", T.AddHours(i), null, [new("Coins", 995, 10, 1)]);
        await ctx.SaveChangesAsync();

        var handler = new SuperiorSlayerHandler(Repo(ctx), new MemoryCache(new MemoryCacheOptions()));

        // Scoped to the three this test seeded — the fixture database is shared, so other tests'
        // superiors are in the table too.
        var mine = new[] { "Crushing hand", "Choke devil", "Colossal Hydra" };
        List<string> Ordered(SuperiorComparison c) =>
            c.Rows.Select(r => r.Name).Where(n => mine.Contains(n)).ToList();

        var byLevel = await handler.Get(SuperiorSort.Default);
        Assert.Equal(["Colossal Hydra", "Choke devil", "Crushing hand"], Ordered(byLevel));

        var byCharacter = await handler.Get(new SuperiorSort(charId));
        Assert.Equal(["Crushing hand", "Choke devil", "Colossal Hydra"], Ordered(byCharacter));

        var ascending = await handler.Get(new SuperiorSort(charId, Ascending: true));
        Assert.Equal(["Colossal Hydra", "Choke devil", "Crushing hand"], Ordered(ascending));

        // Ascending by level is the mirror of the default.
        Assert.Equal(
            Ordered(byLevel).AsEnumerable().Reverse().ToList(),
            Ordered(await handler.Get(new SuperiorSort(Ascending: true))));
    }

    [Fact]
    public async Task An_unknown_sort_character_falls_back_to_the_default_ordering()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-badsort");
        Seed.AddKill(ctx, userId, charId, "Night beast", T, null, [new("Coins", 995, 10, 1)]);
        await ctx.SaveChangesAsync();

        var handler = new SuperiorSlayerHandler(Repo(ctx), new MemoryCache(new MemoryCacheOptions()));

        // The id comes off a query string, so a stale bookmark or a since-hidden character is an
        // ordinary thing to receive. It must fall back, not throw and not empty the table.
        var bogus = await handler.Get(new SuperiorSort(CharacterId: -999));
        var levels = bogus.Rows.Select(r => r.SlayerLevel).ToList();

        Assert.NotEmpty(bogus.Rows);
        Assert.Equal(levels.OrderByDescending(l => l).ToList(), levels);
    }

    [Fact]
    public async Task A_characters_column_carries_their_most_recent_superior()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-recent");

        // Newest is on a DIFFERENT monster from the oldest, so a per-monster max would not do.
        Seed.AddKill(ctx, userId, charId, "King kurask", T, null, [new("Coins", 995, 10, 1)]);
        Seed.AddKill(ctx, userId, charId, "Marble gargoyle", T.AddDays(40), null, [new("Coins", 995, 10, 1)]);
        Seed.AddKill(ctx, userId, charId, "King kurask", T.AddDays(9), null, [new("Coins", 995, 10, 1)]);
        await ctx.SaveChangesAsync();

        var handler = new SuperiorSlayerHandler(Repo(ctx), new MemoryCache(new MemoryCacheOptions()));
        var comparison = await handler.Get();

        var column = comparison.Characters.Single(c => c.GameCharacterId == charId);
        Assert.Equal(T.AddDays(40), column.LastKilled);
    }

    // ---------------------------------------------------------------- weekly activity

    [Fact]
    public async Task Weekly_activity_buckets_kills_by_week()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-weeks");

        // Three in one week, one in the week after. Wednesday and Thursday of the same week must
        // land in the same bucket; the following Monday must not.
        var wednesday = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
        foreach (var offset in new[] { 0, 1, 2 })
            Seed.AddKill(ctx, userId, charId, "Night beast", wednesday.AddDays(offset), null,
                [new("Coins", 995, 10, 1)]);
        Seed.AddKill(ctx, userId, charId, "Night beast", wednesday.AddDays(6), null,
            [new("Coins", 995, 10, 1)]);
        await ctx.SaveChangesAsync();

        var weeks = (await Repo(ctx).GetWeeklyActivity(["night beast"], 5000))
            .OrderBy(w => w.WeekStart)
            .ToList();

        Assert.Equal(2, weeks.Count);
        Assert.Equal(3, weeks[0].Kills);
        Assert.Equal(1, weeks[1].Kills);
        Assert.Equal(7, (weeks[1].WeekStart - weeks[0].WeekStart).TotalDays);
        Assert.Equal(DayOfWeek.Monday, weeks[0].WeekStart.DayOfWeek);
    }

    [Fact]
    public async Task Weekly_activity_ignores_admin_baselines()
    {
        // THE POINT OF THE SEPARATE READ. A baseline is a lump sum with no date on it, so it cannot
        // belong to a week; folding it in would invent a spike on whatever week the query happened
        // to attribute it to. GetCounts and GetBaseMonsterKills both include baselines on purpose -
        // this one must not, and nothing else would catch it being added.
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-weeks-baseline");

        ctx.CharacterSourceBaselines.Add(new CharacterSourceBaseline
        {
            GameCharacterId = charId,
            SourceName = "Choke devil",
            BaselineKc = 500
        });
        Seed.AddKill(ctx, userId, charId, "Choke devil", T.AddDays(3), null, [new("Coins", 995, 10, 1)]);
        await ctx.SaveChangesAsync();

        var weeks = await Repo(ctx).GetWeeklyActivity(["choke devil"], 5000);

        Assert.Single(weeks);
        Assert.Equal(1, weeks[0].Kills);
    }

    [Fact]
    public async Task Weekly_activity_honours_the_window_and_visibility()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-weeks-window");
        var (hiddenUser, hiddenChar) = await Seed.UserAndCharacter(ctx, "sup-weeks-hidden");
        var hidden = await ctx.GameCharacters.FindAsync(hiddenChar);
        hidden!.IsVisible = false;

        Seed.AddKill(ctx, userId, charId, "Spiked Turoth", DateTimeOffset.UtcNow.AddDays(-3), null,
            [new("Coins", 995, 10, 1)]);
        Seed.AddKill(ctx, userId, charId, "Spiked Turoth", DateTimeOffset.UtcNow.AddDays(-400), null,
            [new("Coins", 995, 10, 1)]);
        Seed.AddKill(ctx, hiddenUser, hiddenChar, "Spiked Turoth", DateTimeOffset.UtcNow.AddDays(-3),
            null, [new("Coins", 995, 10, 1)]);
        await ctx.SaveChangesAsync();

        var weeks = await Repo(ctx).GetWeeklyActivity(["spiked turoth"], 4);

        // The 400-day-old kill is outside a four-week window, and the hidden character never counts.
        Assert.Single(weeks);
        Assert.Equal(1, weeks[0].Kills);
    }

    [Fact]
    public async Task Unique_drops_are_the_table_items_only_and_carry_who_and_when()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-uniques");

        // A unique off a superior, an ordinary drop off the same superior, and a unique off a
        // monster that is not a superior at all. Only the first may come back.
        Seed.AddKill(ctx, userId, charId, "Colossal Hydra", T, null,
            [new("Imbued heart", 20724, 1, 120_000_000)]);
        Seed.AddKill(ctx, userId, charId, "Colossal Hydra", T.AddDays(1), null,
            [new("Coins", 995, 5000, 1)]);
        Seed.AddKill(ctx, userId, charId, "Vorkath", T.AddDays(2), null,
            [new("Imbued heart", 20724, 1, 120_000_000)]);
        await ctx.SaveChangesAsync();

        var drops = await Repo(ctx).GetUniqueDrops(
            SuperiorSlayerMonsters.LoweredNames, SuperiorSlayerMonsters.LoweredUniqueTable);
        var mine = drops.Where(d => d.GameCharacterId == charId).ToList();

        var only = Assert.Single(mine);
        Assert.Equal("Imbued heart", only.ItemName);
        Assert.Equal("colossal hydra", only.SourceKey);
        Assert.Equal(T, only.OccurredAt);
    }

    [Fact]
    public async Task Unique_drops_skip_hidden_characters()
    {
        // Same visibility rule as every other read on this page - a hidden character contributes
        // nothing, and a unique is exactly the sort of thing that would be noticed if it leaked.
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sup-uniques-hidden");
        var character = await ctx.GameCharacters.FindAsync(charId);
        character!.IsVisible = false;

        Seed.AddKill(ctx, userId, charId, "Shadow Wyrm", T, null,
            [new("Eternal gem", 21270, 1, 45_000_000)]);
        await ctx.SaveChangesAsync();

        var drops = await Repo(ctx).GetUniqueDrops(
            SuperiorSlayerMonsters.LoweredNames, SuperiorSlayerMonsters.LoweredUniqueTable);

        Assert.DoesNotContain(drops, d => d.GameCharacterId == charId);
    }
}
