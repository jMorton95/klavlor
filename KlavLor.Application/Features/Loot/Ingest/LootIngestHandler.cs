using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Source;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Ingest;

public sealed class LootIngestHandler(
    ILootRecordRepository lootRecordRepository,
    IGameCharacterRepository gameCharacterRepository,
    LootIngestValidator validator,
    ICurrentUser currentUser,
    ILootFeedService lootFeedService,
    IUserRepository userRepository,
    ICollectionLogCache collectionLogCache,
    IMemoryCache memoryCache)
{
    private static readonly string[] DateFormats =
    [
        "MMM dd, yyyy, h:mm:ss tt",
        "MMM d, yyyy, h:mm:ss tt",
        "MMM dd, yyyy, h:mm:ss a",
        "MMM d, yyyy, h:mm:ss a"
    ];

    public async Task<Result> Handle(LootIngestCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var userId = currentUser.UserId;
        if (userId is null)
            return Result.Failure("User not authenticated.");

        var character = await ResolveCharacter(userId.Value, command.CharacterId);

        var parsed = MapToLootRecord(command, userId.Value, character?.Id);
        if (parsed is null)
            return Result.Failure("Failed to parse loot data.");

        var (record, drops) = parsed;

        if (record.ContentHash is not null)
        {
            var existing = await lootRecordRepository.FindExistingHashes(userId.Value, [record.ContentHash]);
            if (existing.Count > 0)
                return Result.Success(); // duplicate, skip
        }

        if (character is not null)
        {
            var seen = await lootRecordRepository.GetSeenItemNames(character.Id, record.OccurredAt);
            drops = drops.Select(d => d with { IsFirstTime = !seen.Contains(d.Name) }).ToList();
        }
        FinalizeDrops(record, drops);

        await lootRecordRepository.SaveLootRecord(record);

        // Imported records can land earlier than existing ones for this character —
        // re-sweep so any pre-existing later records lose their stale IsFirstTime flag.
        if (record.IsImported && character is not null)
            await lootRecordRepository.RecomputeFirstTimeFlags(character.Id);

        if (character is not null)
        {
            LootStatsCache.Invalidate(memoryCache, character.Id);
            // Source pages aggregate over all characters — bump the global source version.
            GlobalSourceCache.Invalidate(memoryCache);
        }

        if (ShouldPublishToFeed(record, character))
            await PublishToFeed(userId.Value, record, character);
        return Result.Success();
    }

    public async Task<Result> HandleBatch(List<LootIngestCommand> commands)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result.Failure("User not authenticated.");

        // Resolve characters for all unique characterIds in the batch.
        var characterCache = new Dictionary<string, GameCharacter?>();
        foreach (var charId in commands.Select(c => c.CharacterId).Where(c => !string.IsNullOrEmpty(c)).Distinct())
        {
            characterCache[charId!] = await ResolveCharacter(userId.Value, charId);
        }

        var parsedItems = new List<(ParsedRecord Parsed, GameCharacter? Character)>();
        foreach (var command in commands)
        {
            var validationResult = await validator.ValidateAsync(command);
            if (!validationResult.IsValid)
                continue;

            var character = command.CharacterId is not null && characterCache.TryGetValue(command.CharacterId, out var cached)
                ? cached : null;

            var parsed = MapToLootRecord(command, userId.Value, character?.Id);
            if (parsed is not null)
                parsedItems.Add((parsed, character));
        }

        if (parsedItems.Count == 0)
            return Result.Failure("No valid records to import.");

        // Deduplicate: find which content hashes already exist in the database.
        var hashes = parsedItems
            .Where(r => r.Parsed.Record.ContentHash is not null)
            .Select(r => r.Parsed.Record.ContentHash!)
            .ToList();

        if (hashes.Count > 0)
        {
            var existing = await lootRecordRepository.FindExistingHashes(userId.Value, hashes);
            if (existing.Count > 0)
            {
                parsedItems = parsedItems
                    .Where(r => r.Parsed.Record.ContentHash is null || !existing.Contains(r.Parsed.Record.ContentHash))
                    .ToList();
                if (parsedItems.Count == 0)
                    return Result.Success(); // all duplicates, nothing to insert
            }
        }

        // Compute IsFirstTime per character. Walking each character's records in
        // OccurredAt order and updating a local seen set keeps in-batch firsts
        // marked only on their earliest receipt. Anything that lands before
        // already-saved records gets fixed by RecomputeFirstTimeFlags below.
        var charactersTouched = new HashSet<int>();
        foreach (var group in parsedItems.GroupBy(p => p.Character?.Id))
        {
            if (group.Key is null) continue;
            var cid = group.Key.Value;
            charactersTouched.Add(cid);

            var orderedByTime = group.OrderBy(p => p.Parsed.Record.OccurredAt).ToList();
            var earliest = orderedByTime[0].Parsed.Record.OccurredAt;
            var seen = await lootRecordRepository.GetSeenItemNames(cid, earliest);

            foreach (var (parsed, _) in orderedByTime)
            {
                var newDrops = new List<LootDrop>(parsed.Drops.Count);
                foreach (var d in parsed.Drops)
                {
                    var first = !seen.Contains(d.Name);
                    newDrops.Add(d with { IsFirstTime = first });
                    if (first) seen.Add(d.Name);
                }
                // Replace Drops list contents in place so the outer reference stays consistent.
                parsed.Drops.Clear();
                parsed.Drops.AddRange(newDrops);
            }
        }

        foreach (var (parsed, _) in parsedItems)
            FinalizeDrops(parsed.Record, parsed.Drops);

        var allRecords = parsedItems.Select(p => p.Parsed.Record).ToList();
        await lootRecordRepository.SaveLootRecords(allRecords);

        // Imports may slot in earlier than existing records — fix per character.
        if (parsedItems.Any(p => p.Parsed.Record.IsImported))
        {
            foreach (var cid in charactersTouched)
                await lootRecordRepository.RecomputeFirstTimeFlags(cid);
        }

        foreach (var cid in charactersTouched)
            LootStatsCache.Invalidate(memoryCache, cid);

        if (charactersTouched.Count > 0)
            GlobalSourceCache.Invalidate(memoryCache);

        var liveRecords = parsedItems
            .Where(p => ShouldPublishToFeed(p.Parsed.Record, p.Character))
            .Select(p => (p.Parsed.Record, p.Character))
            .ToList();
        if (liveRecords.Count > 0)
        {
            var user = await userRepository.GetById(userId.Value);
            var userName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";
            foreach (var (record, character) in liveRecords)
            {
                await PublishRecordToFeed(userName, record, character);
            }
        }

        return Result.Success();
    }

    private async Task<GameCharacter?> ResolveCharacter(int userId, string? characterId)
    {
        if (string.IsNullOrEmpty(characterId))
            return null;

        var existing = await gameCharacterRepository.GetByUserAndRuneLiteId(userId, characterId);
        if (existing is not null)
            return existing;

        var newCharacter = new GameCharacter
        {
            UserId = userId,
            RuneLiteId = characterId,
            DisplayName = Guid.NewGuid().ToString(),
            IsVisible = false
        };

        return await gameCharacterRepository.Save(newCharacter);
    }

    private static bool ShouldPublishToFeed(LootRecord record, GameCharacter? character)
    {
        if (!IsCharacterVisible(character))
            return false;

        // Imported records only publish if any single drop qualifies for Rare+ (1M+) to avoid flooding.
        if (record.IsImported)
        {
            var drops = JsonSerializer.Deserialize<List<LootDrop>>(record.DropsJson) ?? [];
            return drops.Any(d => (long)d.Quantity * d.Price >= 1_000_000);
        }

        return true;
    }

    private static bool IsCharacterVisible(GameCharacter? character)
    {
        // Records without a character are always visible (legacy data).
        if (character is null)
            return true;

        return character.IsVisible && !character.IsAdminHidden;
    }

    private async Task PublishToFeed(int userId, LootRecord record, GameCharacter? character)
    {
        var user = await userRepository.GetById(userId);
        var userName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";
        await PublishRecordToFeed(userName, record, character);
    }

    private async Task PublishRecordToFeed(string userName, LootRecord record, GameCharacter? character)
    {
        var drops = JsonSerializer.Deserialize<List<LootDrop>>(record.DropsJson) ?? [];
        var feedDrops = drops.Select(d => new LootFeedDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime, collectionLogCache.IsCollectionLogItem(d.ItemId))).ToList();
        var dropsByTier = ILootFeedService.ClassifyDropsByTier(feedDrops);

        // Chronological ordinal — only needed as a fallback label when RuneLite
        // didn't supply a KillCount. Compute once per record regardless of tier
        // since all tiers share the same ordinal.
        int? ordinal = null;
        if (character is not null && record.Id > 0)
            ordinal = await lootRecordRepository.GetKillOrdinal(
                character.Id, record.SourceName, record.OccurredAt, record.Id);

        // Route to the matching feed scope. Characters without IsLeagues set default to Main.
        var scope = character?.IsLeagues == true ? LootFeedScope.Leagues : LootFeedScope.Main;

        foreach (var (tier, tierDrops) in dropsByTier)
        {
            // Skip Standard/Uncommon tiers for imported records to avoid flooding.
            if (record.IsImported && tier is LootFeedTier.Standard or LootFeedTier.Uncommon)
                continue;

            var tierTotal = tierDrops.Sum(d => (long)d.Quantity * d.Price);
            lootFeedService.Publish(new LootFeedEntry(
                userName,
                record.UserId,
                record.SourceName,
                record.SourceType,
                tierTotal,
                tierDrops,
                record.OccurredAt,
                tier,
                character?.GetEffectiveName(userName),
                character?.Id,
                MinKillCount: record.KillCount,
                MaxKillCount: record.KillCount,
                MinKillOrdinal: ordinal,
                MaxKillOrdinal: ordinal,
                Scope: scope));
        }
    }

    private static ParsedRecord? MapToLootRecord(LootIngestCommand command, int userId, int? gameCharacterId)
    {
        if (!Enum.TryParse<LootSourceType>(command.Type, ignoreCase: true, out var sourceType))
            sourceType = LootSourceType.Unknown;

        DateTimeOffset occurredAt;
        if (DateTime.TryParseExact(
                command.Date, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localDt))
        {
            occurredAt = IngestTimezone.FromLocalNaive(localDt);
        }
        else
        {
            occurredAt = DateTimeOffset.UtcNow;
        }

        var drops = command.Drops.Select(d => new LootDrop(d.Name, d.Id, d.Quantity, d.Price)).ToList();
        var totalValue = drops.Sum(d => (long)d.Quantity * d.Price);

        var record = new LootRecord
        {
            UserId = userId,
            SourceName = command.Name,
            SourceType = sourceType,
            CombatLevel = command.Level == -1 ? null : command.Level,
            KillCount = command.KillCount == -1 ? null : command.KillCount,
            TotalValue = totalValue,
            DropsJson = "[]", // finalized after IsFirstTime is applied
            OccurredAt = occurredAt,
            ContentHash = command.ContentHash,
            IsImported = command.Imported,
            GameCharacterId = gameCharacterId
        };

        return new ParsedRecord(record, drops);
    }

    private sealed record ParsedRecord(LootRecord Record, List<LootDrop> Drops);

    private static void FinalizeDrops(LootRecord record, List<LootDrop> drops)
    {
        record.DropsJson = JsonSerializer.Serialize(drops);
    }
}
