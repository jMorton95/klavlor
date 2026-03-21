using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Log;

public sealed record LootLogUserSummary(
    int UserId,
    string UserName,
    int TotalSources,
    long TotalKills,
    long TotalValue);

public sealed record LootLogSearchResult(
    List<LootSourceSummary> SourceMatches,
    List<LootItemMatch> ItemMatches,
    bool HasMore = false,
    string? SearchTerm = null,
    int TotalCount = 0);

public sealed record LootSourceSummary(
    string SourceName,
    LootSourceType SourceType,
    int TotalKills,
    long TotalValue,
    List<LootDropSummary> TopDrops);

public sealed record LootItemMatch(
    string SourceName,
    string SourceType,
    int TotalKills,
    string ItemName,
    long TotalQuantity,
    long TotalItemValue);

public sealed record LootDropSummary(
    string Name,
    long TotalQuantity,
    long TotalValue);

public sealed record LootSourceDetail(
    string SourceName,
    LootSourceType SourceType,
    int TotalKills,
    long TotalValue,
    List<LootDropSummary> AllDrops,
    List<LootKillEntry> Kills,
    bool HasMore,
    int TotalCount = 0,
    List<LootKillEntry>? NotableDrops = null);

public sealed record LootKillEntry(
    DateTimeOffset OccurredAt,
    int? KillCount,
    long TotalValue,
    List<LootKillDrop> Drops);

public sealed record LootKillDrop(
    string Name,
    int Quantity,
    int Price);
