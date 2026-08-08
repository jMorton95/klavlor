using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

[Collection("postgres")]
public sealed class CharacterSessionsTests(PostgresFixture fx)
{
    [Fact]
    public async Task GetCharacterSessions_groups_per_source_and_interleaves_newest_first()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "csessions");
        var t = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

        // Source A (Vorkath): two kills close together -> session A1.
        Seed.AddKill(ctx, userId, charId, "CS_Vorkath", t, 1, [new("Bones", 1, 1, 100)]);
        Seed.AddKill(ctx, userId, charId, "CS_Vorkath", t.AddMinutes(5), 2,
            [new("Visage", 2, 1, 5_000_000, IsFirstTime: true)]);
        // Source B (Zulrah): one kill interleaved in time -> session B1. Value >=10k so the
        // single-kill one-off filter (GetCharacterSessions) keeps it; this test is about grouping.
        Seed.AddKill(ctx, userId, charId, "CS_Zulrah", t.AddMinutes(10), 1, [new("Scale", 3, 100, 200)]);
        // Source A again after a >16h gap -> session A2 (most recent overall). Also >=10k value.
        Seed.AddKill(ctx, userId, charId, "CS_Vorkath", t.AddHours(17), 3, [new("Bones", 1, 1, 15_000)]);
        await ctx.SaveChangesAsync();

        // FakeClogCache(2): only ItemId 2 (Visage) counts as a collection-log item.
        var repo = new LootSessionRepository(ctx, NullLogger<LootSessionRepository>.Instance, new FakeClogCache(2), new FakeItemValueCache());
        var history = await repo.GetCharacterSessions(charId, 1, 20);

        Assert.Equal(3, history.TotalSessions);
        Assert.Equal(3, history.Sessions.Count);

        // Interleaved newest-first by session end: A2 (t+17h), then B1 (t+10m), then A1.
        Assert.Equal("CS_Vorkath", history.Sessions[0].SourceName);
        Assert.Equal(1, history.Sessions[0].Session.KillCount);
        Assert.Equal("CS_Zulrah", history.Sessions[1].SourceName);
        Assert.Equal("CS_Vorkath", history.Sessions[2].SourceName);
        Assert.Equal(2, history.Sessions[2].Session.KillCount);

        // Per-source session numbering: Vorkath's oldest run is #1, its later run #2.
        Assert.Equal(2, history.Sessions[0].Session.Index);
        Assert.Equal(1, history.Sessions[2].Session.Index);

        // The first Vorkath session aggregates its drops; Visage is a first-time clog NEW.
        Assert.Contains(history.Sessions[2].Session.TopDrops,
            d => d is { Name: "Visage", IsCollectionLogItem: true, IsFirstTime: true });
    }
}
