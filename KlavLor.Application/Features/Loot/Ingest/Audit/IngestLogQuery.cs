namespace KlavLor.Application.Features.Loot.Ingest.Audit;

/// <summary>
/// Query for the admin "Sync Log" audit page. Lists ingested loot records newest-ingest-first.
/// <paramref name="IncludeBackfill"/> defaults to true (show everything); set false to hide
/// historical/backfill records and show only the live continuous stream.
/// </summary>
public sealed record IngestLogQuery(int PageNumber = 1, int PageSize = 50, bool IncludeBackfill = true);
