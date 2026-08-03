using System.Text.Json;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace KlavLor.IntegrationTests;

// Minimal ICollectionLogCache stand-in: fixed sets of "is a collection-log item" ids and names.
// Matches on either, like the real cache, because the two don't always agree for the same item.
internal sealed class FakeClogCache(params int[] ids) : ICollectionLogCache
{
    private readonly HashSet<int> _ids = [.. ids];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public bool IsCollectionLogItem(int itemId, string? itemName = null) =>
        _ids.Contains(itemId) || (!string.IsNullOrEmpty(itemName) && _names.Contains(itemName));

    public void Replace(IEnumerable<CollectionLogEntryRef> entries)
    {
        _ids.Clear();
        _names.Clear();
        foreach (var e in entries)
        {
            _ids.Add(e.ItemId);
            if (!string.IsNullOrWhiteSpace(e.Name)) _names.Add(e.Name);
        }
    }
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

    // Registers an item as a collection-log entry (the EffectiveCollectionLogItems view
    // reads through to this table), optionally mapped to source "tabs".
    public static void AddClogItem(DataContext ctx, int itemId, string name, params string[] tabs)
    {
        ctx.CollectionLogItems.Add(new CollectionLogItem
        {
            ItemId = itemId,
            Name = name,
            Tabs = tabs.Length > 0 ? tabs : null,
            SyncedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
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
