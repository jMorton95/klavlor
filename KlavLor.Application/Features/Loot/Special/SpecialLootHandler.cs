using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.CollectionLog;
using KlavLor.Application.Features.Drop;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Source;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Special;

public sealed record SpecialLootCharacterOption(int Id, string Name);

// Admin-only injection of untradeable "special" drops (Infernal Cape, Dizana's Quiver) that
// RuneLite never logs. Reuses the ordinary loot storage + feed plumbing: it writes a normal
// LootRecord + LootDropRow (so the collection log picks it up by item id automatically) but
// flags the drop IsSpecial with zero value, which forces it to the top feed tier and the giga
// visual effect. Deliberately does NOT go through LootIngestHandler, because that binds the
// record to the current user; here an admin targets any character and the record is owned by
// that character's user.
public sealed class SpecialLootHandler(
    IGameCharacterRepository gameCharacterRepository,
    ILootRecordRepository lootRecordRepository,
    IUserRepository userRepository,
    ILootFeedService lootFeedService,
    ICollectionLogCache collectionLogCache,
    CollectionLogAdminHandler clogAdmin,
    IMemoryCache memoryCache)
{
    public async Task<List<SpecialLootCharacterOption>> GetCharacters()
    {
        var characters = await gameCharacterRepository.GetSelectable();
        return characters
            .Select(c => new SpecialLootCharacterOption(c.Id, c.GetEffectiveName()))
            .ToList();
    }

    // Reuse the existing collection-log search so the item field only ever resolves to a real
    // clog item (which guarantees a valid item id for the log to match on).
    public Task<List<ClogItemRow>> SearchItems(string? term) => clogAdmin.Search(term);

    public async Task<Result> Inject(int characterId, string itemName, string sourceName, DateTimeOffset occurredAt, bool announce)
    {
        itemName = (itemName ?? "").Trim();
        sourceName = (sourceName ?? "").Trim();
        if (itemName.Length == 0 || sourceName.Length == 0)
            return Result.Failure("Item and source are both required.");

        var character = await gameCharacterRepository.GetById(characterId);
        if (character is null)
            return Result.Failure("Character not found.");

        // Resolve to the real collection-log item id — an exact name match, else refuse rather
        // than guess, so we never store an unresolvable drop.
        var matches = await clogAdmin.Search(itemName);
        var item = matches.FirstOrDefault(m => string.Equals(m.Name, itemName, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return Result.Failure($"'{itemName}' is not a known collection-log item.");

        var ownerUserId = character.UserId;
        // Synthetic, deterministic hash: keeps this out of the RuneLite hash space and makes
        // re-submitting the same item for the same character and time idempotent.
        var contentHash = $"manual:{characterId}:{item.ItemId}:{occurredAt.UtcTicks}";
        var existing = await lootRecordRepository.FindExistingHashes(ownerUserId, [contentHash]);
        if (existing.Count > 0)
            return Result.Success(); // already injected — no-op

        var seen = await lootRecordRepository.GetSeenItemNames(character.Id, occurredAt);
        var isFirstTime = !seen.Contains(item.Name);

        var drop = new LootDrop(item.Name, item.ItemId, Quantity: 1, Price: 0, IsFirstTime: isFirstTime, IsSpecial: true);

        var record = new LootRecord
        {
            UserId = ownerUserId,
            SourceName = sourceName,
            SourceType = LootSourceType.Npc,
            KillCount = null,
            TotalValue = 0,
            OccurredAt = occurredAt,
            ContentHash = contentHash,
            IsImported = false,
            GameCharacterId = character.Id,
            DropsJson = JsonSerializer.Serialize(new List<LootDrop> { drop })
        };
        record.ReplaceDropRows(
        [
            new LootDropRow
            {
                ItemId = drop.ItemId,
                Name = drop.Name,
                Quantity = 1,
                Price = 0,
                IsFirstTime = isFirstTime,
                IsSpecial = true
            }
        ]);

        await lootRecordRepository.SaveLootRecord(record);
        // Back-dated inserts can land before existing records — re-sweep first-time flags.
        await lootRecordRepository.RecomputeFirstTimeFlags(character.Id);

        LootStatsCache.Invalidate(memoryCache, character.Id);
        GlobalSourceCache.Invalidate(memoryCache, record.SourceName);
        GlobalDropCache.Invalidate(memoryCache, drop.Name);

        // Optionally headline the live feed now, independent of the (possibly historical)
        // logged time. Only for visible characters, matching the ordinary feed's rule.
        if (announce && character.IsVisible && !character.IsAdminHidden)
        {
            var owner = await userRepository.GetById(ownerUserId);
            var ownerName = owner is not null ? $"{owner.FirstName} {owner.LastName}" : "Unknown";
            var scope = character.IsLeagues ? LootFeedScope.Leagues : LootFeedScope.Main;
            var feedDrop = new LootFeedDrop(drop.Name, 1, 0, isFirstTime,
                collectionLogCache.IsCollectionLogItem(drop.ItemId, drop.Name), IsSpecial: true);

            lootFeedService.Publish(new LootFeedEntry(
                ownerName,
                ownerUserId,
                record.SourceName,
                record.SourceType,
                0,
                [feedDrop],
                occurredAt,
                LootFeedTier.Legendary,
                character.GetEffectiveName(ownerName),
                character.Id,
                Scope: scope));
        }

        return Result.Success();
    }
}
