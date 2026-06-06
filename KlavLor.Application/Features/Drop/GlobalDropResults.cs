using KlavLor.Application.Common;
using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Drop;

// Aggregates for the global (all-players) drop page. Mirrors the Source feature, but pivots
// the aggregation from "one source → its items" to "one item → its sources & characters".
// Every query aggregates over visible, non-admin-hidden, non-Leagues characters only —
// the same visibility rules as the Source page and the global search.

public sealed record GlobalDropOverview(
    string ItemName,
    int ItemId,
    long TotalDrops,        // kills that yielded this item, across all visible characters
    long TotalQuantity,
    long TotalValue,
    int DistinctSources,
    int DistinctCharacters,
    int DistinctPlayers,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen);

// One source that drops this item. Drops = kills at the source that yielded the item;
// Kills = total kills recorded at the source. Observed rate = Drops / Kills, shown next to
// the wiki Rarity columns for an "expected vs actual" comparison.
public sealed record DropSourceRow(
    string SourceName,
    LootSourceType SourceType,
    long Drops,
    long Kills,
    long TotalQuantity,
    long TotalValue,
    string? Rarity,
    int? RarityNumerator,
    int? RarityDenominator,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);

public sealed record DropSourceTable(
    IReadOnlyList<DropSourceRow> Rows,
    int TotalSources,
    long TotalDrops,
    long TotalQuantity,
    long TotalValue,
    string? SearchTerm,
    string SortBy,
    SortDirection SortDirection);

// One character that has received this item.
public sealed record DropCharacterRow(
    int GameCharacterId,
    string CharacterName,
    string UserName,
    long Drops,
    long TotalQuantity,
    long TotalValue,
    int DistinctSources,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);

public sealed record DropCharacterTable(
    IReadOnlyList<DropCharacterRow> Rows,
    int TotalCharacters,
    long TotalDrops,
    long TotalQuantity,
    long TotalValue,
    string? SearchTerm,
    string SortBy,
    SortDirection SortDirection);

// A play session (across all visible characters) that yielded this item at least once,
// newest first. Same per-(character, source) grouping as the character profile's session
// history, so the existing session-kills modal works unchanged via (GameCharacterId,
// SourceName, SessionIndex). Item-focused: ItemDrops/ItemQuantity/ItemValue summarise only
// the page's item within the session; SessionKills is the full session size for context.
// KillCount range is RuneLite's real KC when reported, else the derived ordinal range.
public sealed record DropSessionRow(
    int GameCharacterId,
    string CharacterName,
    string SourceName,
    LootSourceType SourceType,
    int SessionIndex,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int SessionKills,
    int? MinKillCount,
    int? MaxKillCount,
    int MinKillOrdinal,
    int MaxKillOrdinal,
    int ItemDrops,
    long ItemQuantity,
    long ItemValue);
