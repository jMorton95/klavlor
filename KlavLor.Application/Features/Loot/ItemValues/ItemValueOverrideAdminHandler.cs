using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Drop;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Features.Source;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Application.Features.Loot.ItemValues;

// Backs the admin "item values" panel: look an item up by name, give it a fixed intrinsic GP value,
// and have that value become the truth for every past and future receipt of it.
//
// Every write follows the same three steps — persist, re-prime the singleton cache, re-derive the
// stored loot projection — so the in-memory path (drops read back out of DropsJson) and the SQL path
// (LootDrops.Price / LootRecords.TotalValue) can never disagree about what an item is worth. The
// memoised aggregates are then invalidated for exactly the characters, sources and items the rebuild
// touched, and the live feed's buffers are reseeded.
//
// THAT LAST STEP IS NOT OPTIONAL, and leaving it out was a shipped bug. Re-priming the cache and
// rebuilding the projection fixes every surface that READS from the database on request; the loot
// feed's swimlanes are an in-memory buffer that is only built at startup, so they went on serving
// entries priced before the write. See FeedBufferSeeder for what that looked like from the outside.
public sealed class ItemValueOverrideAdminHandler(
    IItemValueOverrideRepository repository,
    IItemValueOverrideCache cache,
    IMemoryCache memoryCache,
    RecomputeTrigger recompute,
    FeedBufferSeeder feedBuffer)
{
    public const int SearchLimit = 25;

    // Deliberately generous: the point of the report is to be scanned once and acted on, and the
    // collection-log items it surfaces sort to the top regardless of how much junk sits below them.
    public const int ZeroValueLimit = 100;

    // Guards against an int overflow in the quantity × price maths downstream. A billion GP per
    // single item is already far beyond anything in the game.
    public const int MaxValue = 1_000_000_000;

    public Task<List<ItemValueOverrideRow>> List() => repository.List();

    public Task<List<ItemValueCandidate>> SearchItems(string? term) => repository.SearchItems(term, SearchLimit);

    // On-request only — see ZeroValueItem. Never wired to a load trigger.
    public Task<List<ZeroValueItem>> FindZeroValueItems() => repository.FindZeroValueItems(ZeroValueLimit);

    public async Task<Result<List<ItemValueOverrideRow>>> Set(int itemId, string? itemName, int value)
    {
        itemName = (itemName ?? "").Trim();
        if (itemId <= 0)
            return Result<List<ItemValueOverrideRow>>.Failure("Pick an item from the search results first.");
        if (itemName.Length == 0)
            return Result<List<ItemValueOverrideRow>>.Failure("Item name is required.");
        if (value < 0 || value > MaxValue)
            return Result<List<ItemValueOverrideRow>>.Failure($"Value must be between 0 and {MaxValue:N0}.");

        // A value of 0 is indistinguishable from "no override" for an untradeable, so treat it as a
        // removal rather than storing a no-op row.
        if (value == 0)
            await repository.Delete(itemId);
        else
            await repository.Upsert(itemId, itemName, value);

        await ApplyAndRebuild(itemId);
        return Result<List<ItemValueOverrideRow>>.Success(await repository.List());
    }

    public async Task<List<ItemValueOverrideRow>> Remove(int itemId)
    {
        await repository.Delete(itemId);
        await ApplyAndRebuild(itemId);
        return await repository.List();
    }

    // Re-prime BEFORE rebuilding: the rebuild re-prices every affected drop through the cache, so
    // the cache has to already reflect the write. Removal restores the raw RuneLite price because
    // DropsJson still holds it — the stored projection is derived, never the source of truth.
    private async Task ApplyAndRebuild(int itemId)
    {
        cache.Replace(await repository.GetAll());

        var rebuilt = await repository.RebuildForItem(itemId);

        foreach (var characterId in rebuilt.CharacterIds)
            LootStatsCache.Invalidate(memoryCache, characterId);
        foreach (var sourceName in rebuilt.SourceNames)
            GlobalSourceCache.Invalidate(memoryCache, sourceName);
        foreach (var itemName in rebuilt.ItemNames)
            GlobalDropCache.Invalidate(memoryCache, itemName);

        // The live feed's swimlanes are an in-memory buffer, not a query. Nothing above touches
        // them, so without this the lanes keep the pre-override pricing until the next restart —
        // and a drop that was under the 10k floor at its raw price is missing from them entirely.
        await feedBuffer.Reseed();

        // The board's 100k-per-receipt entry floor reads this value, so a newly-valued item can
        // qualify for a slot it previously failed on.
        await recompute.LuckInputsChanged();
    }
}
