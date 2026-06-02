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
public sealed record SourceClogEvent(
    string CharacterName,
    int GameCharacterId,
    string ItemName,
    DateTimeOffset OccurredAt,
    int? KillCount);

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

public sealed record SourceTrendCharacter(string CharacterName, long Kills);
