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
    List<LootKillEntry>? NotableDrops = null,
    string? CharacterName = null);

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
    bool IsFirstTime = false,
    bool IsCollectionLogItem = false);

// A continuous play session at a source: consecutive kills with no gap longer than
// LootFeedGrouping.MaxGap (1h). MinKillCount/MaxKillCount are the real RuneLite KC range
// when reported; MinKillOrdinal/MaxKillOrdinal are the derived position range (1 = oldest
// kill at this source), used as the honest "Kill #a–b" fallback.
public sealed record LootSession(
    int Index,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int KillCount,
    int? MinKillCount,
    int? MaxKillCount,
    int MinKillOrdinal,
    int MaxKillOrdinal,
    long TotalValue,
    List<LootKillDrop> TopDrops,
    int DistinctDropCount);

public sealed record LootSourceSessions(
    string SourceName,
    LootSourceType SourceType,
    string? CharacterName,
    int TotalKills,
    long TotalValue,
    List<LootSession> Sessions,
    bool HasMore,
    int TotalSessions);
