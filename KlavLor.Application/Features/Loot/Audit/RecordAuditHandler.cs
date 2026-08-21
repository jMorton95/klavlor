using KlavLor.Application.Common;
using KlavLor.Application.Features.Drop;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Loot.Special;
using KlavLor.Application.Features.Source;
using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Interfaces.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace KlavLor.Application.Features.Loot.Audit;

/// <summary>
/// Backs the admin record-audit panel: narrow to a character and a source, page through their
/// records, and either delete a single bad one or take it out of the luck maths.
///
/// The case it exists for is RuneLite mis-attributing a drop — opening a dossier at the moment an
/// item was equipped logs that item as loot from the dossier. Before this, the only deletion
/// available was every record for a character, which throws away good data to remove one bad row.
/// </summary>
public sealed class RecordAuditHandler(
    ILootRecordAuditRepository repository,
    IGameCharacterRepository characters,
    IMemoryCache memoryCache,
    RecomputeTrigger recompute)
{
    /// <summary>Page sizes offered in the UI. Bounded rather than free-form: the rows carry their
    /// drops, so an unbounded size is a way to ask for the whole table by accident.</summary>
    public static readonly int[] PageSizes = [10, 25, 50, 100];

    public const int DefaultPageSize = 25;

    public async Task<List<SpecialLootCharacterOption>> GetCharacters()
    {
        var chars = await characters.GetSelectable();
        return chars.Select(c => new SpecialLootCharacterOption(c.Id, c.GetEffectiveName())).ToList();
    }

    public Task<List<AuditSourceOption>> GetSources(int characterId) =>
        characterId > 0 ? repository.GetSources(characterId) : Task.FromResult(new List<AuditSourceOption>());

    public async Task<AuditRecordPage> Search(int characterId, string? sourceName, string? term, int page, int pageSize)
    {
        var source = (sourceName ?? "").Trim();
        if (characterId <= 0 || source.Length == 0)
            return new AuditRecordPage([], 1, DefaultPageSize, 0);

        // Clamp rather than trust: these arrive as query-string values.
        var size = PageSizes.Contains(pageSize) ? pageSize : DefaultPageSize;
        return await repository.Search(characterId, source, (term ?? "").Trim(), Math.Max(1, page), size);
    }

    /// <summary>
    /// Delete one record entirely. Its drops go with it through the existing cascade. For a record
    /// whose kill was real but whose drop cannot be rated, use <see cref="SetLuckExclusion"/>.
    ///
    /// A deleted record changes both sides of every luck ratio for that character and source — the
    /// roll count and, if it carried the item, the receipt — so the leaderboard is flagged for
    /// rebuild and the memoised aggregates it fed are dropped. Without that the site would keep
    /// quoting figures derived from a record the admin has just decided was never real.
    /// </summary>
    public async Task<Result> Delete(int recordId)
    {
        var deleted = await repository.Delete(recordId);
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

    /// Drop every memoised aggregate the record fed and ask for a leaderboard rebuild. Shared by
    /// delete and exclude so the two can't invalidate different things for the same change of fact.
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
