using System.Globalization;
using System.Text.Json;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Ingest;

public sealed class LootIngestHandler(
    ILootRecordRepository lootRecordRepository,
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

        var record = MapToLootRecord(command, userId.Value);
        if (record is null)
            return Result.Failure("Failed to parse loot data.");

        if (record.ContentHash is not null)
        {
            var existing = await lootRecordRepository.FindExistingHashes(userId.Value, [record.ContentHash]);
            if (existing.Count > 0)
                return Result.Success(); // duplicate, skip
        }

        await lootRecordRepository.SaveLootRecord(record);
        if (!record.IsImported)
            await PublishToFeed(userId.Value, record);
        return Result.Success();
    }

    public async Task<Result> HandleBatch(List<LootIngestCommand> commands)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result.Failure("User not authenticated.");

        var records = new List<LootRecord>();
        foreach (var command in commands)
        {
            var validationResult = await validator.ValidateAsync(command);
            if (!validationResult.IsValid)
                continue;

            var record = MapToLootRecord(command, userId.Value);
            if (record is not null)
                records.Add(record);
        }

        if (records.Count == 0)
            return Result.Failure("No valid records to import.");

        // Deduplicate: find which content hashes already exist in the database.
        var hashes = records
            .Where(r => r.ContentHash is not null)
            .Select(r => r.ContentHash!)
            .ToList();

        if (hashes.Count > 0)
        {
            var existing = await lootRecordRepository.FindExistingHashes(userId.Value, hashes);
            if (existing.Count > 0)
            {
                records = records.Where(r => r.ContentHash is null || !existing.Contains(r.ContentHash)).ToList();
                if (records.Count == 0)
                    return Result.Success(); // all duplicates, nothing to insert
            }
        }

        await lootRecordRepository.SaveLootRecords(records);

        var liveRecords = records.Where(r => !r.IsImported).ToList();
        if (liveRecords.Count > 0)
        {
            var user = await userRepository.GetById(userId.Value);
            var userName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";
            foreach (var record in liveRecords)
            {
                PublishRecordToFeed(userName, record);
            }
        }

        return Result.Success();
    }

    private async Task PublishToFeed(int userId, LootRecord record)
    {
        var user = await userRepository.GetById(userId);
        var userName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";
        PublishRecordToFeed(userName, record);
    }

    private void PublishRecordToFeed(string userName, LootRecord record)
    {
        var drops = JsonSerializer.Deserialize<List<LootDrop>>(record.DropsJson) ?? [];
        var feedDrops = drops.Select(d => new LootFeedDrop(d.Name, d.Quantity, d.Price)).ToList();

        lootFeedService.Publish(new LootFeedEntry(
            userName,
            record.SourceName,
            record.SourceType,
            record.TotalValue,
            feedDrops,
            record.OccurredAt));
    }

    private static LootRecord? MapToLootRecord(LootIngestCommand command, int userId)
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
            IsImported = command.Imported
        };
    }
}
