using System.Globalization;
using System.Text.Json;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Feed;
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
    IUserRepository userRepository)
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

        var record = MapToLootRecord(command, userId.Value, character?.Id);
        if (record is null)
            return Result.Failure("Failed to parse loot data.");

        if (record.ContentHash is not null)
        {
            var existing = await lootRecordRepository.FindExistingHashes(userId.Value, [record.ContentHash]);
            if (existing.Count > 0)
                return Result.Success(); // duplicate, skip
        }

        await lootRecordRepository.SaveLootRecord(record);
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

        var records = new List<(LootRecord Record, GameCharacter? Character)>();
        foreach (var command in commands)
        {
            var validationResult = await validator.ValidateAsync(command);
            if (!validationResult.IsValid)
                continue;

            var character = command.CharacterId is not null && characterCache.TryGetValue(command.CharacterId, out var cached)
                ? cached : null;

            var record = MapToLootRecord(command, userId.Value, character?.Id);
            if (record is not null)
                records.Add((record, character));
        }

        if (records.Count == 0)
            return Result.Failure("No valid records to import.");

        var allRecords = records.Select(r => r.Record).ToList();

        // Deduplicate: find which content hashes already exist in the database.
        var hashes = allRecords
            .Where(r => r.ContentHash is not null)
            .Select(r => r.ContentHash!)
            .ToList();

        if (hashes.Count > 0)
        {
            var existing = await lootRecordRepository.FindExistingHashes(userId.Value, hashes);
            if (existing.Count > 0)
            {
                records = records.Where(r => r.Record.ContentHash is null || !existing.Contains(r.Record.ContentHash)).ToList();
                allRecords = records.Select(r => r.Record).ToList();
                if (allRecords.Count == 0)
                    return Result.Success(); // all duplicates, nothing to insert
            }
        }

        await lootRecordRepository.SaveLootRecords(allRecords);

        var liveRecords = records.Where(r => ShouldPublishToFeed(r.Record, r.Character)).ToList();
        if (liveRecords.Count > 0)
        {
            var user = await userRepository.GetById(userId.Value);
            var userName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";
            foreach (var (record, character) in liveRecords)
            {
                PublishRecordToFeed(userName, record, character);
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
        PublishRecordToFeed(userName, record, character);
    }

    private void PublishRecordToFeed(string userName, LootRecord record, GameCharacter? character)
    {
        var drops = JsonSerializer.Deserialize<List<LootDrop>>(record.DropsJson) ?? [];
        var feedDrops = drops.Select(d => new LootFeedDrop(d.Name, d.Quantity, d.Price)).ToList();
        var dropsByTier = ILootFeedService.ClassifyDropsByTier(feedDrops);

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
                character?.Id));
        }
    }

    private static LootRecord? MapToLootRecord(LootIngestCommand command, int userId, int? gameCharacterId)
    {
        if (!Enum.TryParse<LootSourceType>(command.Type, ignoreCase: true, out var sourceType))
            sourceType = LootSourceType.Unknown;

        if (!DateTimeOffset.TryParseExact(
                command.Date, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var occurredAt))
        {
            occurredAt = DateTimeOffset.UtcNow;
        }

        var drops = command.Drops.Select(d => new LootDrop(d.Name, d.Id, d.Quantity, d.Price)).ToList();
        var totalValue = drops.Sum(d => (long)d.Quantity * d.Price);
        var dropsJson = JsonSerializer.Serialize(drops);

        return new LootRecord
        {
            UserId = userId,
            SourceName = command.Name,
            SourceType = sourceType,
            CombatLevel = command.Level == -1 ? null : command.Level,
            KillCount = command.KillCount == -1 ? null : command.KillCount,
            TotalValue = totalValue,
            DropsJson = dropsJson,
            OccurredAt = occurredAt,
            ContentHash = command.ContentHash,
            IsImported = command.Imported,
            GameCharacterId = gameCharacterId
        };
    }
}
