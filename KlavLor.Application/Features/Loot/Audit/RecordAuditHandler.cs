using KlavLor.Application.Common;
using KlavLor.Application.Features.Drop;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Loot.Special;
using KlavLor.Application.Features.Source;
using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;

namespace KlavLor.Application.Features.Loot.Audit;

/// <summary>
/// Backs the admin record-audit panel: narrow to a character, page through their records, and
/// repair one — delete it, take it out of the luck maths, or hide a single drop on it.
///
/// The case it exists for is RuneLite mis-attributing a drop — opening a dossier at the moment an
/// item was equipped logs that item as loot from the dossier. Before this, the only deletion
/// available was every record for a character, which throws away good data to remove one bad row.
/// </summary>
public sealed class RecordAuditHandler(
    ILootRecordAuditRepository repository,
    IGameCharacterRepository characters,
    IDropBlacklistCache blacklistCache,
    IMemoryCache memoryCache,
    FeedBufferSeeder feedBuffer,
    RecomputeTrigger recompute)
{
    /// <summary>Page sizes offered in the UI. Bounded rather than free-form: the rows carry their
    /// drops, so an unbounded size is a way to ask for the whole table by accident.</summary>
    public static readonly int[] PageSizes = [10, 25, 50, 100];

    public const int DefaultPageSize = 25;

    /// <summary>How many manual changes the modifications list shows. Generous enough to be a
    /// history, small enough that it stays a list you read rather than one you page through.</summary>
    public const int ModificationsLimit = 100;

    public async Task<List<SpecialLootCharacterOption>> GetCharacters()
    {
        var chars = await characters.GetSelectable();
        return chars.Select(c => new SpecialLootCharacterOption(c.Id, c.GetEffectiveName())).ToList();
    }

    public Task<List<AuditSourceOption>> GetSources(int characterId) =>
        characterId > 0 ? repository.GetSources(characterId) : Task.FromResult(new List<AuditSourceOption>());

    /// <summary>
    /// A source OR a search term is enough. Requiring both made the commonest hunt impossible:
    /// the source a mis-attributed drop was filed under is exactly what the admin cannot guess, so
    /// "find every Crystal armour seed this character has" has to work without one. A bare
    /// character with neither still returns nothing rather than their whole history.
    /// </summary>
    public Task<AuditRecordPage> Search(int characterId, string? sourceName, string? term, int page, int pageSize)
    {
        var source = (sourceName ?? "").Trim();
        var needle = (term ?? "").Trim();
        if (characterId <= 0 || (source.Length == 0 && needle.Length == 0))
            return Task.FromResult(new AuditRecordPage([], 1, DefaultPageSize, 0));

        // Clamp rather than trust: these arrive as query-string values.
        var size = PageSizes.Contains(pageSize) ? pageSize : DefaultPageSize;
        return repository.Search(characterId, source, needle, Math.Max(1, page), size);
    }

    public Task<List<AuditModification>> GetModifications() => repository.GetModifications(ModificationsLimit);

    /// <summary>
    /// Delete one record entirely. Its drops go with it through the existing cascade. For a record
    /// whose kill was real but whose drop cannot be rated, use <see cref="SetLuckExclusion"/>; for
    /// one bad item on an otherwise real kill, use <see cref="SetDropBlacklist"/>.
    ///
    /// A deleted record changes both sides of every luck ratio for that character and source — the
    /// roll count and, if it carried the item, the receipt — so the leaderboard is flagged for
    /// rebuild and the memoised aggregates it fed are dropped. Without that the site would keep
    /// quoting figures derived from a record the admin has just decided was never real.
    /// </summary>
    public async Task<Result> Delete(int recordId, string? reason = null)
    {
        var deleted = await repository.Delete(recordId, reason);
        if (deleted is null) return Result.Failure("That record no longer exists.");

        return await Invalidate(deleted);
    }

    /// <summary>
    /// Take one record's drops out of the luck maths, or put them back, without touching the record.
    ///
    /// The kill still counts as a roll and the drop still shows everywhere it did — kill history,
    /// drop grids, value totals, feed cards. What goes is the luck claim: the leaderboard skips the
    /// receipt, the character page's collection panel skips it, and the feed card drops its
    /// lucky/dry line. That is the repair for a receipt we cannot rate honestly rather than one
    /// that never happened; deletion is still the tool for the latter.
    ///
    /// Invalidates and re-flags exactly what a delete does, because it changes the same inputs.
    /// </summary>
    public async Task<Result> SetLuckExclusion(int recordId, bool excluded)
    {
        var changed = await repository.SetLuckExclusion(recordId, excluded);
        if (changed is null) return Result.Failure("That record no longer exists.");

        return await Invalidate(changed);
    }

    /// <summary>
    /// Hide ONE drop on one record from the entire site, or restore it.
    ///
    /// The harder sibling of <see cref="SetLuckExclusion"/>, and deliberately a different unit. That
    /// one disowns a whole record's luck attribution while the drop keeps counting for gold and
    /// stays visible; this one removes a single item outright — the drop grid, the loot chart, GP
    /// totals, the collection log, the live feed, the global item and source pages. The kill still
    /// counts as a roll and every other drop on it is untouched.
    ///
    /// It is for an item that is not loot at all: RuneLite logging something as it is equipped and
    /// attributing it to whatever was opened at that moment. Deleting the record would throw away a
    /// real kill to remove one phantom item.
    ///
    /// Reversible, because DropsJson is never rewritten — restoring re-derives the drop back out of
    /// it exactly as it was.
    /// </summary>
    public async Task<Result> SetDropBlacklist(
        int recordId, int itemId, string? itemName, bool blacklisted, string? reason = null)
    {
        var name = (itemName ?? "").Trim();
        if (name.Length == 0) return Result.Failure("An item name is required.");

        var changed = await repository.SetDropBlacklist(recordId, itemId, name, blacklisted, reason);
        if (changed is null) return Result.Failure("That record no longer exists.");

        // Re-prime before anything reads: the repository has already rebuilt the stored projection,
        // but every call site that reads a kill back out of DropsJson goes through
        // EffectiveDropReader, which asks this cache. A stale cache would leave the drop showing on
        // exactly those surfaces — the feed card, the session list, the biggest-kill panel — while
        // the database agreed it was gone.
        blacklistCache.Replace(await repository.GetAllBlacklistedDrops());

        var result = await Invalidate(changed);

        // The live feed's swimlanes are an in-memory buffer, not a query, so nothing above reaches
        // them: re-priming a cache and re-deriving the database fixes every surface that reads on
        // request and leaves the lanes exactly as they were, still holding a card that shows the
        // drop. Shipping this without the reseed is the same bug the item-value override shipped
        // with, and it looks identical from outside — a card that refuses to change until a restart.
        await feedBuffer.Reseed();

        return result;
    }

    /// Drop every memoised aggregate the record fed and ask for a leaderboard rebuild. Shared by
    /// delete, exclude and blacklist so the three can't invalidate different things for the same
    /// change of fact.
    private async Task<Result> Invalidate(DeletedRecordInfo record)
    {
        LootStatsCache.Invalidate(memoryCache, record.GameCharacterId);
        GlobalSourceCache.Invalidate(memoryCache, record.SourceName);
        foreach (var item in record.ItemNames.Distinct(StringComparer.OrdinalIgnoreCase))
            GlobalDropCache.Invalidate(memoryCache, item);

        await recompute.LuckInputsChanged();
        return Result.Success();
    }
}
