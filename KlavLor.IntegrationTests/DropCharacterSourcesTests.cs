using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Drop;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// The per-character drop breakdown behind the "Drops" button on the drop page's character table.
// The gap it fills: that table could say a character had six of something across three sources,
// then send you to their whole profile with no way to learn which three.
[Collection("postgres")]
public sealed class DropCharacterSourcesTests(PostgresFixture fx)
{
    [Fact]
    public async Task It_returns_only_this_characters_sources_for_this_item()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dcs-mine");
        var (otherUserId, otherCharId) = await Seed.UserAndCharacter(ctx, "dcs-other");
        var t = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        const string item = "DCS Visage";

        // Mine: 3 from Vorkath (over 10 rolls) and 1 from Skeletal Wyvern (over 4 rolls).
        for (var i = 1; i <= 10; i++)
            Seed.AddKill(ctx, userId, charId, "DCS_Vorkath", t.AddMinutes(i), i,
                i <= 3 ? [new(item, 80_001, 2, 1_000)] : [new("DCS Bones", 80_002, 1, 10)]);
        for (var i = 1; i <= 4; i++)
            Seed.AddKill(ctx, userId, charId, "DCS_Wyvern", t.AddHours(2).AddMinutes(i), i,
                i == 1 ? [new(item, 80_001, 1, 1_000)] : [new("DCS Bones", 80_002, 1, 10)]);
        // A different source that never gave me the item — must not appear.
        Seed.AddKill(ctx, userId, charId, "DCS_Zulrah", t.AddHours(4), 1, [new("DCS Scale", 80_003, 5, 20)]);
        // Another character's drops of the same item — must not appear either.
        for (var i = 1; i <= 7; i++)
            Seed.AddKill(ctx, otherUserId, otherCharId, "DCS_Vorkath", t.AddMinutes(i).AddSeconds(30), i,
                [new(item, 80_001, 1, 1_000)]);
        await ctx.SaveChangesAsync();

        var repo = new GlobalDropRepository(ctx, NullLogger<GlobalDropRepository>.Instance);
        var result = await repo.GetCharacterSources(item, charId);

        Assert.NotNull(result);
        Assert.Equal(charId, result!.GameCharacterId);
        Assert.Equal(2, result.Rows.Count);

        // Most drops first.
        var vorkath = result.Rows[0];
        Assert.Equal("DCS_Vorkath", vorkath.SourceName);
        Assert.Equal(3, vorkath.Drops);
        Assert.Equal(6, vorkath.TotalQuantity);      // 3 receipts × 2
        Assert.Equal(6_000, vorkath.TotalValue);
        Assert.Equal(10, vorkath.Kills);             // MY rolls at the source, not everyone's

        var wyvern = result.Rows[1];
        Assert.Equal("DCS_Wyvern", wyvern.SourceName);
        Assert.Equal(1, wyvern.Drops);
        Assert.Equal(4, wyvern.Kills);

        // Totals span only this character.
        Assert.Equal(4, result.TotalDrops);
        Assert.Equal(7, result.TotalQuantity);
        Assert.Equal(7_000, result.TotalValue);
    }

    [Fact]
    public async Task A_character_with_no_receipts_returns_null()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dcs-none");
        Seed.AddKill(ctx, userId, charId, "DCS_Empty", new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero), 1,
            [new("DCS Something else", 80_011, 1, 100)]);
        await ctx.SaveChangesAsync();

        var repo = new GlobalDropRepository(ctx, NullLogger<GlobalDropRepository>.Instance);

        Assert.Null(await repo.GetCharacterSources("DCS Never dropped", charId));
    }

    [Fact]
    public async Task A_hidden_character_is_not_exposed()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "dcs-hidden", visible: false);
        const string item = "DCS Hidden item";
        Seed.AddKill(ctx, userId, charId, "DCS_Hidden", new DateTimeOffset(2026, 6, 3, 10, 0, 0, TimeSpan.Zero), 1,
            [new(item, 80_021, 1, 100)]);
        await ctx.SaveChangesAsync();

        var repo = new GlobalDropRepository(ctx, NullLogger<GlobalDropRepository>.Instance);

        // Same visibility rule as every other global-drop query.
        Assert.Null(await repo.GetCharacterSources(item, charId));
    }
}
