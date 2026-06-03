using KlavLor.Application.Features.CollectionLog;

namespace KlavLor.Application.Interfaces.Repositories;

// Admin-curated blacklist of items excluded from collection-log treatment.
public interface ICollectionLogExclusionRepository
{
    // Blank/short term → the currently-excluded items; otherwise clog items matching the
    // name, each flagged with whether it is excluded.
    Task<List<ClogItemRow>> Search(string? term, int limit);
    Task Exclude(int itemId, string itemName);
    Task Include(int itemId);
}
