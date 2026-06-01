using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Log;

public sealed record LootLogCharacterSummary(
    int GameCharacterId,
    string CharacterName,
    string UserName,
    int TotalSources,
    long TotalKills,
    long TotalValue);

public sealed record LootLogSearchResult(
    List<LootSourceSummary> SourceMatches,
    List<LootItemAggregate> ItemMatches,
    bool HasMore = false,
    string? SearchTerm = null,
    int TotalCount = 0);

public sealed record LootSourceSummary(
    string SourceName,
    LootSourceType SourceType,
    int TotalKills,
    long TotalValue,
    List<LootDropSummary> TopDrops);

public sealed record LootItemAggregate(
    string ItemName,
    long TotalQuantity,
    long TotalValue,
    int SourceCount,
    List<LootItemSourceBreakdown> Sources);

public sealed record LootItemSourceBreakdown(
    string SourceName,
    string SourceType,
    int TotalKills,
    long Quantity,
    long Value);

public sealed record LootDropSummary(
    string Name,
    long TotalQuantity,
    long TotalValue,
    string? Rarity = null,
    int? RarityNumerator = null,
    int? RarityDenominator = null);

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
    int? KillOrdinal,
    long TotalValue,
    List<LootKillDrop> Drops);

public sealed record LootKillDrop(
    string Name,
    int Quantity,
    int Price,
    bool IsFirstTime = false);
