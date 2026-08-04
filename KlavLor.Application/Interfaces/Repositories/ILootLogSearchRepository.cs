using KlavLor.Application.Features.Loot.Ingest.Audit;
using KlavLor.Application.Features.Loot.Log;

namespace KlavLor.Application.Interfaces.Repositories;

// The admin sync log, the public character list, and the per-character log search + sources table.
// One of the five repositories ILootLogRepository was split into, grouped by consumer feature:
// LootLogHandler's search surfaces and IngestLogHandler read through this one.
public interface ILootLogSearchRepository
{
    Task<List<LootLogCharacterSummary>> GetCharactersWithLoot(bool includeHidden = false);
    Task<IngestLogResult> GetIngestLog(IngestLogQuery query);
    Task<LootLogSearchResult> SearchLootLog(int characterId, LootLogQuery query);

    // Data-dense, server-side-sortable per-character sources table (one aggregated row per
    // source) with totals across the full matching set.
    Task<SourceTable> GetCharacterSourceTable(int characterId, LootLogQuery query);
}
