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
/// records, and delete a single bad one.
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
    /// Delete one record. Its drops go with it through the existing cascade.
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

        LootStatsCache.Invalidate(memoryCache, deleted.GameCharacterId);
        GlobalSourceCache.Invalidate(memoryCache, deleted.SourceName);
        foreach (var item in deleted.ItemNames.Distinct(StringComparer.OrdinalIgnoreCase))
            GlobalDropCache.Invalidate(memoryCache, item);

        await recompute.LuckInputsChanged();
        return Result.Success();
    }
}
