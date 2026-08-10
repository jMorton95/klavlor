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
    DateTimeOffset LastSeen,
    // Loot-table rolls per kill for this item — needed so the effective rate matches the character
    // page's, which reads Rolls from DropRates. Hardcoding 1 understated multi-roll tables.
    int Rolls = 1,
    // Rate after this source's loot model and any admin rate modifier, e.g. "1/540". Admin rate
    // modifiers are a global baseline, so this — not the raw wiki Rarity — is what the page shows.
    string? EffectiveRarity = null);

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

// One source that gave ONE character this item — the per-character breakdown the drop page's
// character table links into. Deliberately not DropSourceRow: there is no rate here, because a
// single character's handful of receipts at a source says nothing about the drop rate, and putting
// an "observed rate" column on three drops would invite exactly that reading.
public sealed record DropCharacterSourceRow(
    string SourceName,
    LootSourceType SourceType,
    long Drops,
    long Kills,
    long TotalQuantity,
    long TotalValue,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);

// Everything the per-character drop page needs: who, which item, and the sources that gave it.
public sealed record DropCharacterSources(
    int GameCharacterId,
    string CharacterName,
    string UserName,
    string ItemName,
    IReadOnlyList<DropCharacterSourceRow> Rows,
    long TotalDrops,
    long TotalQuantity,
    long TotalValue,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen);

// One month of this item's activity across all visible players: total drops + gp value, plus
// a per-character breakdown where each character further breaks down into the sources that
// produced its drops that month (drives the trend chart's hover detail).
public sealed record DropTrendPoint(
    int Year,
    int Month,
    long Drops,
    long Value,
    IReadOnlyList<DropTrendCharacter> Characters);

public sealed record DropTrendCharacter(
    string CharacterName,
    long Drops,
    IReadOnlyList<DropTrendSource> Sources);

public sealed record DropTrendSource(string SourceName, long Drops);

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
