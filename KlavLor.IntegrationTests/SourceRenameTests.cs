using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

[Collection("postgres")]
public sealed class SourceRenameTests(PostgresFixture fx)
{
    [Fact]
    public async Task Preview_then_rename_merges_into_existing_and_clears_derived_rows()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "merge");
        var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 3; i++)
            Seed.AddKill(ctx, userId, charId, "MG_From", at.AddMinutes(i), null, [new("x", 1, 1, 10)]);
        for (var i = 0; i < 2; i++)
            Seed.AddKill(ctx, userId, charId, "MG_To", at.AddHours(2).AddMinutes(i), null, [new("y", 2, 1, 20)]);
        ctx.DropRates.Add(new DropRate { SourceName = "MG_From", ItemName = "x", Rarity = "1/100", Rolls = 1, SyncedAt = at });
        await ctx.SaveChangesAsync();

        var repo = new SourceAdminRepository(ctx, NullLogger<SourceAdminRepository>.Instance);

        var preview = await repo.PreviewRename("MG_From", "MG_To");
        Assert.True(preview.IsMerge);
        Assert.Equal(3, preview.RecordsToMove);
        Assert.Equal(2, preview.TargetExistingRecords);
        Assert.Equal(1, preview.DropRatesAffected);

        var moved = await repo.RenameSource("MG_From", "MG_To");
        Assert.Equal(3, moved);

        await using var verify = fx.CreateContext();
        Assert.Equal(0, await verify.LootRecords.CountAsync(r => r.SourceName == "MG_From"));
        Assert.Equal(5, await verify.LootRecords.CountAsync(r => r.SourceName == "MG_To"));
        Assert.Equal(0, await verify.DropRates.CountAsync(d => d.SourceName == "MG_From"));
    }

    [Fact]
    public async Task Rename_to_a_new_name_moves_all_records_without_merge()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "plain");
        var at = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 4; i++)
            Seed.AddKill(ctx, userId, charId, "PR_Old", at.AddMinutes(i), null, [new("z", 3, 1, 5)]);
        await ctx.SaveChangesAsync();

        var repo = new SourceAdminRepository(ctx, NullLogger<SourceAdminRepository>.Instance);

        var preview = await repo.PreviewRename("PR_Old", "PR_New");
        Assert.False(preview.IsMerge);
        Assert.Equal(4, preview.RecordsToMove);

        var moved = await repo.RenameSource("PR_Old", "PR_New");
        Assert.Equal(4, moved);

        await using var verify = fx.CreateContext();
        Assert.Equal(4, await verify.LootRecords.CountAsync(r => r.SourceName == "PR_New"));
        Assert.Equal(0, await verify.LootRecords.CountAsync(r => r.SourceName == "PR_Old"));
    }
}
