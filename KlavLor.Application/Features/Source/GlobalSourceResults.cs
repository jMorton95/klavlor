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

// Community collection-log coverage for a source: how many of its collection-log items
// have been obtained by at least one visible character. Total is 0 when the source has
// no wiki-mapped clog tab (degrades to "no clog mapping" in the UI).
public sealed record GlobalSourceCoverage(int Unlocked, int Total);
