using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// GetKillOrdinals resolves a whole ingest batch in one round-trip, replacing the two-queries-per-
// record GetKillOrdinal on the publish path. Both the feed card and the live roll ticker read their
// roll number from it, so a disagreement between it and the single-record version is the exact
// drift CLAUDE.md keeps warning about - these tests pin them together.
[Collection("postgres")]
public sealed class KillOrdinalBatchTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    private static LootRecordRepository Repo(KlavLor.Infrastructure.Persistence.EntityFramework.DataContext ctx)
        => new(ctx, NullLogger<LootRecordRepository>.Instance);

    [Fact]
    public async Task The_batch_resolver_agrees_with_the_single_record_one()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "ord-agree");

        var records = new List<LootRecord>();
        foreach (var i in Enumerable.Range(0, 6))
            records.Add(Seed.AddKill(ctx, userId, charId, "OB_Vorkath", T.AddMinutes(i), null,
                [new("Bones", 1, 1, 100)]));
        // A second source interleaved in time - the ordinal is per (character, source), so these
        // must not shift Vorkath's numbering.
        foreach (var i in Enumerable.Range(0, 3))
            Seed.AddKill(ctx, userId, charId, "OB_Zulrah", T.AddMinutes(i), null, [new("Scale", 3, 1, 10)]);
        await ctx.SaveChangesAsync();

        var repo = Repo(ctx);
        var requests = records
            .Select(r => new KillOrdinalRequest(r.Id, charId, r.SourceName, r.OccurredAt))
            .ToList();

        var batch = await repo.GetKillOrdinals(requests);

        Assert.Equal(records.Count, batch.Count);
        foreach (var record in records)
        {
            var single = await repo.GetKillOrdinal(charId, record.SourceName, record.OccurredAt, record.Id);
            Assert.Equal(single, batch[record.Id]);
        }

        // And the numbering itself is 1..6 in chronological order.
        Assert.Equal([1, 2, 3, 4, 5, 6], records.Select(r => batch[r.Id]).ToArray());
    }

    [Fact]
    public async Task Records_sharing_a_timestamp_are_tie_broken_by_id()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "ord-tie");

        // Identical OccurredAt. Without the "= and Id <=" tie-break both would claim the same
        // ordinal, and two chips on the ticker would read "#1".
        var first = Seed.AddKill(ctx, userId, charId, "OB_Tie", T, null, [new("Bones", 1, 1, 100)]);
        var second = Seed.AddKill(ctx, userId, charId, "OB_Tie", T, null, [new("Bones", 1, 1, 100)]);
        var third = Seed.AddKill(ctx, userId, charId, "OB_Tie", T, null, [new("Bones", 1, 1, 100)]);
        await ctx.SaveChangesAsync();

        var batch = await Repo(ctx).GetKillOrdinals(
        [
            new KillOrdinalRequest(first.Id, charId, "OB_Tie", T),
            new KillOrdinalRequest(second.Id, charId, "OB_Tie", T),
            new KillOrdinalRequest(third.Id, charId, "OB_Tie", T)
        ]);

        Assert.Equal([1, 2, 3], new[] { batch[first.Id], batch[second.Id], batch[third.Id] });
    }

    [Fact]
    public async Task An_admin_baseline_is_added_to_every_ordinal_at_that_source()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "ord-base");

        var withBaseline = Seed.AddKill(ctx, userId, charId, "OB_Gargoyle", T, null, [new("Bones", 1, 1, 100)]);
        var withoutBaseline = Seed.AddKill(ctx, userId, charId, "OB_Kurask", T, null, [new("Bones", 1, 1, 100)]);
        ctx.CharacterSourceBaselines.Add(new CharacterSourceBaseline
        {
            GameCharacterId = charId,
            SourceName = "OB_Gargoyle",
            BaselineKc = 500
        });
        await ctx.SaveChangesAsync();

        var batch = await Repo(ctx).GetKillOrdinals(
        [
            new KillOrdinalRequest(withBaseline.Id, charId, "OB_Gargoyle", T),
            new KillOrdinalRequest(withoutBaseline.Id, charId, "OB_Kurask", T)
        ]);

        // The baseline applies per source, not per character.
        Assert.Equal(501, batch[withBaseline.Id]);
        Assert.Equal(1, batch[withoutBaseline.Id]);
    }

    [Fact]
    public async Task Two_characters_at_one_source_are_numbered_independently()
    {
        await using var ctx = fx.CreateContext();
        var (aUser, aChar) = await Seed.UserAndCharacter(ctx, "ord-a");
        var (bUser, bChar) = await Seed.UserAndCharacter(ctx, "ord-b");

        var aFirst = Seed.AddKill(ctx, aUser, aChar, "OB_Shared", T, null, [new("Bones", 1, 1, 100)]);
        var bOnly = Seed.AddKill(ctx, bUser, bChar, "OB_Shared", T.AddMinutes(1), null, [new("Bones", 1, 1, 100)]);
        var aSecond = Seed.AddKill(ctx, aUser, aChar, "OB_Shared", T.AddMinutes(2), null, [new("Bones", 1, 1, 100)]);
        await ctx.SaveChangesAsync();

        var batch = await Repo(ctx).GetKillOrdinals(
        [
            new KillOrdinalRequest(aFirst.Id, aChar, "OB_Shared", T),
            new KillOrdinalRequest(bOnly.Id, bChar, "OB_Shared", T.AddMinutes(1)),
            new KillOrdinalRequest(aSecond.Id, aChar, "OB_Shared", T.AddMinutes(2))
        ]);

        Assert.Equal(1, batch[aFirst.Id]);
        Assert.Equal(1, batch[bOnly.Id]);   // B's own first, not the third kill at the source
        Assert.Equal(2, batch[aSecond.Id]);
    }

    [Fact]
    public async Task An_empty_batch_does_no_work()
    {
        await using var ctx = fx.CreateContext();
        Assert.Empty(await Repo(ctx).GetKillOrdinals([]));
    }
}
