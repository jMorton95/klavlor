using KlavLor.Application.Common;
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
    int? RarityDenominator = null,
    // Value of the single biggest receipt of this item, NOT the running total. Feed tiers are
    // classified per drop, so the source page can only mark an item as rare/epic/legendary if one
    // individual drop reached that band — 500 cheap drops summing to millions must not qualify.
    long BestDropValue = 0);

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

// A play session at a source: consecutive kills broken only by a gap over LootFeedGrouping.MaxGap
// (16h) or an overnight break (>= 6h crossing a 06:00 play-day boundary). MinKillCount/MaxKillCount are the real RuneLite KC range
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

// One session in a character's cross-source history: a per-source run (same grouping as a
// source's Kill Sessions, but interleaved across every source the character has killed).
// Session.Index is the per-source session number, so the existing GetSessionKills expand
// works unchanged when given (SourceName, Index).
public sealed record CharacterSession(
    string SourceName,
    LootSourceType SourceType,
    LootSession Session);

public sealed record CharacterSessionHistory(
    List<CharacterSession> Sessions,
    bool HasMore,
    int TotalSessions);

// One row of the per-character sources table — a source the character has loot from, with
// the metrics surfaced in the data-dense sortable view.
public sealed record SourceTableRow(
    string SourceName,
    LootSourceType SourceType,
    long Kills,
    long TotalValue,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int Sessions,
    int DistinctItems,
    long TotalDrops,
    string? BiggestDropName,
    long BiggestDropValue,
    int ClogUnlocked,
    int ClogTotal);

// Aggregate footer across every source matching the current filter (not just the page).
public sealed record SourceTableTotals(
    int Sources,
    long Kills,
    long TotalValue,
    long DistinctItems,
    long TotalDrops);

public sealed record SourceTable(
    List<SourceTableRow> Rows,
    SourceTableTotals Totals,
    bool HasMore,
    int TotalSources,
    string? SearchTerm,
    string SortBy,
    SortDirection SortDirection);
