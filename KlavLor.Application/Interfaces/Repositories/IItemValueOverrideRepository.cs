using KlavLor.Application.Features.Loot.ItemValues;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Interfaces.Repositories;

// What a rebuild touched, so the caller can invalidate exactly the memoised aggregates that are now
// stale rather than flushing everything.
public sealed record ItemValueRebuildResult(
    int RecordsUpdated,
    IReadOnlyList<int> CharacterIds,
    IReadOnlyList<string> SourceNames,
    IReadOnlyList<string> ItemNames);

// CRUD for the admin-set intrinsic item values, plus the re-derivation of the stored loot
// projection that has to follow every write.
public interface IItemValueOverrideRepository
{
    // Every configured override with the number of loot records it affects.
    Task<List<ItemValueOverrideRow>> List();

    // Lookup over items that have actually been dropped, so an override can only ever name
    // something the site knows about. Blank term returns nothing.
    Task<List<ItemValueCandidate>> SearchItems(string? term, int limit);

    // Items that have been dropped but never at a non-zero price, most-dropped first. A full scan
    // of the drop table, so it is only ever run when an admin explicitly asks for it.
    Task<List<ZeroValueItem>> FindZeroValueItems(int limit);

    Task Upsert(int itemId, string itemName, int value);

    Task Delete(int itemId);

    // Full snapshot for priming the cache.
    Task<IReadOnlyList<ItemValueOverrideValue>> GetAll();

    // Re-derives LootDrops.Price and LootRecords.TotalValue for every record containing the item,
    // reading the raw RuneLite prices back out of DropsJson and re-applying the override cache.
    // Symmetric by construction: setting, changing and removing an override all run the same pass,
    // and removal restores the raw price because that is what DropsJson still holds. Call AFTER the
    // cache has been re-primed.
    Task<ItemValueRebuildResult> RebuildForItem(int itemId);
}
