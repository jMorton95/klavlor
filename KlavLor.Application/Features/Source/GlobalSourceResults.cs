using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Source;

// Aggregates for the global (all-players) source page. Mirrors the per-character source
// detail in LootLogRepository but drops the GameCharacterId filter, summing across every
// visible character that has killed the source.

public sealed record GlobalSourceOverview(
    string SourceName,
    LootSourceType SourceType,
    long TotalKills,
    long TotalValue,
    int DistinctCharacters,
    int DistinctPlayers,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen);

public sealed record GlobalSourceDrop(
    string ItemName,
    long TotalQuantity,
    long TotalValue,
    string? Rarity,
    int? RarityNumerator,
    int? RarityDenominator);

public sealed record SourcePlayerRow(
    int GameCharacterId,
    string CharacterName,
    string UserName,
    long TotalKills,
    long TotalValue);

// A historical "first-time collection-log item" event: the moment a character first
// received a collection-log item from this source. Newest first.
// KillCount is the real in-game KC RuneLite reported (usually absent); KillOrdinal is
// the derived position in this character's kills at the source (always present) — shown
// as a "Kill #N" fallback, matching the per-character LootLogKillEntry convention.
public sealed record SourceClogEvent(
    string CharacterName,
    int GameCharacterId,
    string ItemName,
    DateTimeOffset OccurredAt,
    int? KillCount,
    int KillOrdinal);

// Every item dropped at a source, ranked by how many times it has dropped (occurrence
// count across all visible characters), with a per-character breakdown for the hover card.
public sealed record SourceItemFrequency(
    string ItemName,
    long TotalDrops,
    IReadOnlyList<SourceItemCharacterCount> Characters);

public sealed record SourceItemCharacterCount(string CharacterName, long Drops);

// One month of activity at a source, aggregated across all visible players, plus the
// per-character kill breakdown shown in the month's hover card.
public sealed record SourceTrendPoint(
    int Year,
    int Month,
    long Kills,
    long Value,
    IReadOnlyList<SourceTrendCharacter> Characters);

public sealed record SourceTrendCharacter(
    string CharacterName,
    long Kills,
    IReadOnlyList<SourceTrendClog> Clogs);

// A collection-log item a character received in a given month at the source.
// KillCount = real RuneLite-reported KC (usually null); KillOrdinal = derived running
// kill position at this source (always present), shown as "Kill #N" when KC is absent.
public sealed record SourceTrendClog(string ItemName, int? KillCount, int KillOrdinal);
