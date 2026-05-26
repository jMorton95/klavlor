using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Log;

public sealed class LootCharacterProfileHandler(
    ILootLogRepository lootLogRepository,
    CharacterAccessChecker accessChecker)
{
    public async Task<Result<ProfileHeader>> HandleHeader(int characterId)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<ProfileHeader>.Failure("Character not found.");

        var header = await lootLogRepository.GetProfileHeader(characterId);
        return header is null
            ? Result<ProfileHeader>.Failure("Character not found.")
            : Result<ProfileHeader>.Success(header);
    }

    public async Task<Result<ProfileWindowStats>> HandleWindowStats(int characterId)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<ProfileWindowStats>.Failure("Character not found.");

        // Sequential awaits — the scoped DbContext can only handle one query at a time.
        var now = DateTimeOffset.UtcNow;
        var last7d = await lootLogRepository.GetWindowStats(characterId, now.AddDays(-7), null);
        var last30d = await lootLogRepository.GetWindowStats(characterId, now.AddDays(-30), null);
        var allTime = await lootLogRepository.GetWindowStats(characterId, null, null);

        return Result<ProfileWindowStats>.Success(new ProfileWindowStats(last7d, last30d, allTime));
    }

    public async Task<Result<HeatmapData>> HandleHeatmap(int characterId, HeatmapMode mode)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<HeatmapData>.Failure("Character not found.");

        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-364); // 365 cells inclusive
        var days = await lootLogRepository.GetActivityCalendar(characterId, from, now.AddDays(1));

        return Result<HeatmapData>.Success(new HeatmapData(
            DateOnly.FromDateTime(from.UtcDateTime),
            DateOnly.FromDateTime(now.UtcDateTime),
            mode,
            days));
    }

    public async Task<Result<PersonalRecords>> HandleRecords(int characterId)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<PersonalRecords>.Failure("Character not found.");

        var records = await lootLogRepository.GetPersonalRecords(characterId);
        return Result<PersonalRecords>.Success(records);
    }

    public async Task<Result<Dictionary<string, int>>> HandleDryStreaks(int characterId, IReadOnlyList<string> sourceNames)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<Dictionary<string, int>>.Success([]);

        var streaks = await lootLogRepository.GetDryStreaks(characterId, sourceNames);
        return Result<Dictionary<string, int>>.Success(streaks);
    }

    public async Task<Result<SourceCollection>> HandleSourceCollection(int characterId, string sourceName)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<SourceCollection>.Failure("Character not found.");

        var collection = await lootLogRepository.GetSourceCollection(characterId, sourceName);
        return Result<SourceCollection>.Success(collection);
    }

    public async Task<Result<FirstTimeFeed>> HandleFirstTimeFeed(int characterId, DateTimeOffset? before, int pageSize)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<FirstTimeFeed>.Success(new FirstTimeFeed([], null, false));

        var feed = await lootLogRepository.GetFirstTimeFeed(characterId, before, pageSize);
        return Result<FirstTimeFeed>.Success(feed);
    }

    public async Task<Result<TopItemsList>> HandleTopItems(int characterId, int limit)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<TopItemsList>.Failure("Character not found.");

        var data = await lootLogRepository.GetTopItems(characterId, limit);
        return Result<TopItemsList>.Success(data);
    }

}
