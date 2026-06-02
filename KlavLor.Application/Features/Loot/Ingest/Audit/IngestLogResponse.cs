using KlavLor.Domain.Entities;

namespace KlavLor.Application.Features.Loot.Ingest.Audit;

public sealed record IngestLogResult(
    List<IngestLogEntry> Entries,
    bool HasMore,
    int TotalCount,
    int LiveCount,
    int BackfillCount);

/// <summary>One ingested loot record (a single kill) as shown on the Sync Log page.</summary>
public sealed record IngestLogEntry(
    int Id,
    DateTimeOffset SavedAt,       // ingest time (when the sync client's data hit the API)
    DateTimeOffset OccurredAt,    // in-game kill time
    string UserName,
    string? CharacterName,        // null for legacy records with no linked character
    string SourceName,
    LootSourceType SourceType,
    int? KillCount,
    bool IsImported,              // true = backfill/historical, false = live stream
    IReadOnlyList<string> ItemNames);
