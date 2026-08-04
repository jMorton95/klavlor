using KlavLor.Application.Features.Loot.Log;

namespace KlavLor.Application.Interfaces.Repositories;

// One character at one source: the detail page and its paged kill list, the hover popover, the
// monthly kill trend, and the collection-log progress panel. One of the five repositories
// ILootLogRepository was split into, grouped by consumer feature.
public interface ILootSourceDetailRepository
{
    Task<LootSourceDetail> GetSourceDetail(int characterId, string sourceName, int pageNumber, int pageSize);
    Task<LootSourceDetail> GetSourceDetailKillsPage(int characterId, string sourceName, int pageNumber, int pageSize);

    Task<SourceCollection> GetSourceCollection(int characterId, string sourceName);
    Task<SourceKillTrend> GetSourceKillTrend(int characterId, string sourceName);
    Task<SourcePopoverData> GetSourcePopover(int characterId, string sourceName);
}
