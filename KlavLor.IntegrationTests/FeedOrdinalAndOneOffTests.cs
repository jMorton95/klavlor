using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

[Collection("postgres")]
public sealed class FeedOrdinalAndOneOffTests(PostgresFixture fx)
{
    // Part A: GetAllFeedTiers no longer computes KillOrdinal per candidate row; it's filled lazily
    // afterwards, only for surviving cards that lack a RuneLite KillCount.
    [Fact]
    public async Task GetAllFeedTiers_fills_kill_ordinal_only_when_killcount_missing()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "ordfeed");
        // Unique source names so assertions are robust to other tests' data in the shared container.
        var noKc = "ORD_NoKc_" + Guid.NewGuid().ToString("N")[..8];
        var withKc = "ORD_WithKc_" + Guid.NewGuid().ToString("N")[..8];
        // June (recent) so these sort to the front of the feed's OccurredAt-desc candidate fetch.
        var t = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        LootDrop drop = new("RuneDrop", 10, 1, 50_000); // 50k => Standard tier (10k–100k)

        // Three kills with NO RuneLite KillCount, within 16h -> one merged card spanning ordinals 1–3.
        Seed.AddKill(ctx, userId, charId, noKc, t, null, [drop]);
        Seed.AddKill(ctx, userId, charId, noKc, t.AddMinutes(30), null, [drop]);
        Seed.AddKill(ctx, userId, charId, noKc, t.AddHours(1), null, [drop]);
        // One kill WITH a KillCount -> uses the KC label, never the ordinal fallback.
        Seed.AddKill(ctx, userId, charId, withKc, t.AddHours(2), 500, [drop]);
        await ctx.SaveChangesAsync();

        var repo = new LootFeedRepository(ctx, NullLogger<LootFeedRepository>.Instance, new FakeClogCache(), new FakeItemValueCache());
        var tiers = await repo.GetAllFeedTiers(200, LootFeedScope.Main);
        var standard = tiers[LootFeedTier.Standard];

        var noKcEntry = standard.Single(e => e.SourceName == noKc);
        Assert.Null(noKcEntry.MinKillCount);
        Assert.Null(noKcEntry.MaxKillCount);
        Assert.Equal(3, noKcEntry.RunCount);          // three kills merged into one card
        Assert.Equal(1, noKcEntry.MinKillOrdinal);    // oldest kill at this source
        Assert.Equal(3, noKcEntry.MaxKillOrdinal);    // newest kill at this source

        var withKcEntry = standard.Single(e => e.SourceName == withKc);
        Assert.Equal(500, withKcEntry.MinKillCount);
        Assert.Null(withKcEntry.MinKillOrdinal);      // KC present -> ordinal not computed
        Assert.Null(withKcEntry.MaxKillOrdinal);
    }

    // Part A2: a card's KC range covers the whole play session (including kills whose drops were
    // below the feed floor and never became candidates), not just the kills that hit the tier —
    // and every tier's card shows the SAME session range.
    [Fact]
    public async Task Feed_card_kc_range_spans_the_whole_session()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sessrange");
        var src = "SESS_Range_" + Guid.NewGuid().ToString("N")[..8];
        var t = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        LootDrop junk = new("Bones", 1, 1, 5_000);      // below the 10k feed floor — never a candidate
        LootDrop valued = new("Gem", 2, 1, 50_000);     // Standard tier (10k–100k)
        LootDrop rare = new("Relic", 3, 1, 2_000_000);  // Rare tier (1M–10M)

        // One session (1h gaps): junk, valued (Standard), rare (Rare), junk.
        Seed.AddKill(ctx, userId, charId, src, t, 100, [junk]);
        Seed.AddKill(ctx, userId, charId, src, t.AddHours(1), 105, [valued]);
        Seed.AddKill(ctx, userId, charId, src, t.AddHours(2), 110, [rare]);
        Seed.AddKill(ctx, userId, charId, src, t.AddHours(3), 120, [junk]);
        await ctx.SaveChangesAsync();

        var repo = new LootFeedRepository(ctx, NullLogger<LootFeedRepository>.Instance, new FakeClogCache(), new FakeItemValueCache());
        var tiers = await repo.GetAllFeedTiers(200, LootFeedScope.Main);

        var standard = tiers[LootFeedTier.Standard].Single(e => e.SourceName == src);
        Assert.Equal(1, standard.RunCount);          // only the valued kill merged into the card
        Assert.Equal(100, standard.MinKillCount);    // ...but the KC range covers the whole session
        Assert.Equal(120, standard.MaxKillCount);
        Assert.Equal(t, standard.GroupAnchorAt);     // the card anchors at the session's first kill
        Assert.Null(standard.MinKillOrdinal);        // KC present -> ordinal fallback not computed

        // The Rare swimlane's card for the same session shows the identical range.
        var rareCard = tiers[LootFeedTier.Rare].Single(e => e.SourceName == src);
        Assert.Equal(100, rareCard.MinKillCount);
        Assert.Equal(120, rareCard.MaxKillCount);
    }

    // Part A3: the live-ingest path's session-bounds lookup (GetSessionBounds) — exercised here
    // because in production it only runs while publishing freshly ingested kills, so a SQL
    // regression would otherwise first surface as broken ingestion.
    [Fact]
    public async Task GetSessionBounds_describes_the_session_containing_the_kill()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "sessbounds");
        var src = "SESS_Bounds_" + Guid.NewGuid().ToString("N")[..8];
        var t = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        LootDrop drop = new("Bones", 1, 1, 5_000);

        // Previous session: one kill 20h earlier (gap > 16h -> break before the current session).
        Seed.AddKill(ctx, userId, charId, src, t.AddHours(-20), 90, [drop]);
        // Current session: three kills an hour apart.
        Seed.AddKill(ctx, userId, charId, src, t, 100, [drop]);
        Seed.AddKill(ctx, userId, charId, src, t.AddHours(1), 105, [drop]);
        Seed.AddKill(ctx, userId, charId, src, t.AddHours(2), 110, [drop]);
        await ctx.SaveChangesAsync();

        var repo = new LootRecordRepository(ctx, NullLogger<LootRecordRepository>.Instance);
        var bounds = await repo.GetSessionBounds(
            charId, src, t.AddHours(2),
            LootFeedGrouping.MaxGap, LootFeedGrouping.SessionBreakGap);

        Assert.NotNull(bounds);
        Assert.Equal(100, bounds!.MinKillCount);   // session starts at the kill after the 20h break
        Assert.Equal(110, bounds.MaxKillCount);
        Assert.Equal(t, bounds.StartedAt);
        Assert.Equal(2, bounds.FirstOrdinal);      // one earlier kill at this source
    }

    // Part B: the character session-history list hides single-kill sessions worth under the floor,
    // but keeps multi-kill sessions and high-value one-offs, and leaves headline totals accurate.
    [Fact]
    public async Task Character_sessions_hide_cheap_one_offs_but_keep_totals_accurate()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "oneoff");
        var cheapSingle = "OO_CheapSingle_" + Guid.NewGuid().ToString("N")[..8];
        var richSingle = "OO_RichSingle_" + Guid.NewGuid().ToString("N")[..8];
        var cheapGrind = "OO_CheapGrind_" + Guid.NewGuid().ToString("N")[..8];
        var t = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

        // One kill, <10k -> hidden as a true one-off.
        Seed.AddKill(ctx, userId, charId, cheapSingle, t, 1, [new("Junk", 1, 1, 5_000)]);
        // One kill, >=10k -> kept (an interesting one-off).
        Seed.AddKill(ctx, userId, charId, richSingle, t.AddMinutes(5), 1, [new("Gem", 2, 1, 50_000)]);
        // Two kills totalling <10k, one session -> kept because it's a multi-kill grind.
        Seed.AddKill(ctx, userId, charId, cheapGrind, t.AddMinutes(10), 1, [new("Bones", 3, 1, 3_000)]);
        Seed.AddKill(ctx, userId, charId, cheapGrind, t.AddMinutes(40), 2, [new("Bones", 3, 1, 3_000)]);
        await ctx.SaveChangesAsync();

        var repo = new LootSessionRepository(ctx, NullLogger<LootSessionRepository>.Instance, new FakeClogCache(), new FakeItemValueCache());
        var profileRepo = new LootProfileRepository(ctx, NullLogger<LootProfileRepository>.Instance, new FakeItemValueCache());

        var history = await repo.GetCharacterSessions(charId, pageNumber: 1, pageSize: 20);
        Assert.Equal(2, history.TotalSessions);
        var sources = history.Sessions.Select(s => s.SourceName).ToHashSet();
        Assert.Contains(richSingle, sources);
        Assert.Contains(cheapGrind, sources);
        Assert.DoesNotContain(cheapSingle, sources); // single kill worth <10k is filtered out

        // Headline totals still count every kill, including the hidden one-off.
        var header = await profileRepo.GetProfileHeader(charId);
        Assert.NotNull(header);
        Assert.Equal(4, header!.TotalKills);          // 1 + 1 + 2
        Assert.Equal(61_000, header.TotalGp);         // 5k + 50k + 3k + 3k
        Assert.Equal(3, header.TotalSources);         // all three sources, hidden or not
    }

    // A continuous run that never pauses long enough to gap-split is still capped at 16h per session.
    [Fact]
    public async Task Character_sessions_cap_a_continuous_run_at_16h()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "cap16h");
        var src = "CAP_Cont_" + Guid.NewGuid().ToString("N")[..8];
        var t = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        // 21 kills, one every 2h for 40h straight: no gap ever reaches 6h (so neither the 16h gap
        // nor the overnight rule fires) -> only the hard 16h duration cap splits it: ceil(40/16) = 3.
        for (var k = 0; k <= 20; k++)
            Seed.AddKill(ctx, userId, charId, src, t.AddHours(2 * k), k + 1, [new("Loot", 1, 1, 2_000)]);
        await ctx.SaveChangesAsync();

        var repo = new LootSessionRepository(ctx, NullLogger<LootSessionRepository>.Instance, new FakeClogCache(), new FakeItemValueCache());
        var history = await repo.GetCharacterSessions(charId, pageNumber: 1, pageSize: 20);

        var mine = history.Sessions.Where(s => s.SourceName == src).ToList();
        Assert.Equal(3, mine.Count); // 0–16h, 16–32h, 32–40h
        Assert.All(mine, s => Assert.True(s.Session.EndedAt - s.Session.StartedAt <= TimeSpan.FromHours(16)));
    }
}
