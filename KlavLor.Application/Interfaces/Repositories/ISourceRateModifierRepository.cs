using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Interfaces.Repositories;

// CRUD for admin-configured source/item rate multipliers.
public interface ISourceRateModifierRepository
{
    // Blank term: the current modifier list. Otherwise: source names with loot, matching the
    // term, each carrying its current source-wide multiplier so the admin can adjust it.
    Task<List<SourceRateModifierRow>> Search(string? term, int limit);

    // Insert or update the multiplier for (source, item). itemName empty = source-wide.
    Task Upsert(string sourceName, string itemName, double multiplier);

    Task Delete(string sourceName, string itemName);

    // Full snapshot for priming the cache.
    Task<IReadOnlyList<SourceRateModifierValue>> GetAll();
}
