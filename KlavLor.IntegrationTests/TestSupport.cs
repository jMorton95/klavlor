using System.Text.Json;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace KlavLor.IntegrationTests;

// Minimal ICollectionLogCache stand-in: a fixed set of "is a collection-log item" ids.
internal sealed class FakeClogCache(params int[] ids) : ICollectionLogCache
{
    private readonly HashSet<int> _ids = [.. ids];
    public bool IsCollectionLogItem(int itemId) => _ids.Contains(itemId);
    public void Replace(IEnumerable<int> itemIds) { _ids.Clear(); foreach (var i in itemIds) _ids.Add(i); }
}

internal static class Seed
{
    private static string Unique(string tag) => $"{tag}-{Guid.NewGuid():N}";

    public static async Task<(int UserId, int CharacterId)> UserAndCharacter(
        DataContext ctx, string tag, bool leagues = false, bool visible = true, bool hidden = false)
    {
        var user = new User("Test", tag, Unique(tag) + "@test.local", true) { HashedPassword = "x" };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var gc = new GameCharacter
        {
            UserId = user.Id,
            RuneLiteId = Unique(tag),
            DisplayName = Unique(tag)[..20],
            IsVisible = visible,
            IsAdminHidden = hidden,
            IsLeagues = leagues
        };
        ctx.GameCharacters.Add(gc);
        await ctx.SaveChangesAsync();
        return (user.Id, gc.Id);
    }

    // Adds a kill via EF the same way ingest's FinalizeDrops does: DropsJson plus the
    // projected LootDrop rows from the same list.
    public static LootRecord AddKill(
        DataContext ctx, int userId, int? characterId, string source, DateTimeOffset at,
        int? killCount, IReadOnlyList<LootDrop> drops, bool projectRows = true)
    {
        var rec = new LootRecord
        {
            UserId = userId,
            GameCharacterId = characterId,
            SourceName = source,
            SourceType = LootSourceType.Npc,
            KillCount = killCount,
            TotalValue = drops.Sum(d => (long)d.Quantity * d.Price),
            DropsJson = JsonSerializer.Serialize(drops),
            OccurredAt = at
        };
        if (projectRows)
            rec.ReplaceDropRows(drops.Select(d => new LootDropRow
            {
                ItemId = d.ItemId,
                Name = d.Name,
                Quantity = d.Quantity,
                Price = d.Price,
                IsFirstTime = d.IsFirstTime
            }));
        ctx.LootRecords.Add(rec);
        return rec;
    }
}
