using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.CollectionLog;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.CollectionLog;

/// <summary>Read side of the Temple-sourced collection log. See ICollectionLogQueryRepository.</summary>
internal sealed class CollectionLogQueryRepository(
    DataContext dataContext, ILogger<CollectionLogQueryRepository> logger) : ICollectionLogQueryRepository
{
    // Same visibility rule as every other public surface: no hidden characters, no Leagues accounts
    // (their logs are seasonal and would distort a main-game comparison).
    private static IQueryable<GameCharacter> Visible(DataContext ctx) =>
        ctx.GameCharacters.AsNoTracking().Where(gc => gc.IsVisible && !gc.IsAdminHidden && !gc.IsLeagues);

    public async Task<List<CollectionLogStanding>> GetStandings()
    {
        try
        {
            // One row per character, straight off the denormalised state table. This is the whole
            // reason that table exists — aggregating the entries table per character on every board
            // render would be thousands of rows each for a number that never changes between syncs.
            var rows = await Visible(dataContext)
                .Join(dataContext.CharacterCollectionLogStates.AsNoTracking(),
                    gc => gc.Id, s => s.GameCharacterId, (gc, s) => new { gc, s })
                .Join(dataContext.Users.AsNoTracking(), x => x.gc.UserId, u => u.Id, (x, u) => new
                {
                    x.gc.Id,
                    CharacterName = x.gc.DisplayName ?? (u.FirstName + " " + u.LastName),
                    UserName = u.FirstName + " " + u.LastName,
                    x.s
                })
                .ToListAsync();

            return rows
                .Select(r => new CollectionLogStanding(
                    r.Id, r.CharacterName, r.UserName, r.s.GameMode,
                    r.s.TotalObtained, r.s.TotalAvailable,
                    r.s.CategoriesFinished, r.s.CategoriesAvailable,
                    r.s.HiscoresRank,
                    Freshness(r.s)))
                // Rank by raw count, not percent: the denominator is the same for everyone, and a
                // percent would let a stale TotalAvailable reorder the board.
                .OrderByDescending(s => s.Obtained)
                .ThenBy(s => s.CharacterName)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get collection log standings");
            throw new RepositoryException("Failed to get collection log standings", ex);
        }
    }

    public async Task<CharacterCollectionLog?> GetCharacterLog(int gameCharacterId)
    {
        try
        {
            var header = await Visible(dataContext)
                .Where(gc => gc.Id == gameCharacterId)
                .Join(dataContext.Users.AsNoTracking(), gc => gc.UserId, u => u.Id, (gc, u) => new
                {
                    gc.Id,
                    CharacterName = gc.DisplayName ?? (u.FirstName + " " + u.LastName)
                })
                .FirstOrDefaultAsync();

            if (header is null) return null;

            var state = await dataContext.CharacterCollectionLogStates.AsNoTracking()
                .FirstOrDefaultAsync(s => s.GameCharacterId == gameCharacterId);

            // Per-category counts in one grouped query over the membership join. ~2,500 membership
            // rows against this character's entries — small enough to do live on every request.
            var obtained = await (
                from ci in dataContext.CollectionLogCategoryItems.AsNoTracking()
                join e in dataContext.CharacterCollectionLogEntries.AsNoTracking()
                    .Where(e => e.GameCharacterId == gameCharacterId)
                    on ci.ItemId equals e.ItemId
                group ci by ci.CategorySlug into g
                select new { Slug = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Slug, x => x.Count);

            var categories = (await dataContext.CollectionLogCategories.AsNoTracking()
                    .Select(c => new { c.Slug, c.DisplayName, c.GroupName, c.ItemCount })
                    .ToListAsync())
                // Ordered here rather than in SQL: the group order is a display decision (raids
                // belong with bosses at the top, not filed under C for clues), not a stored column.
                .OrderBy(c => CollectionLogGroups.SortOrder(c.GroupName))
                .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var icons = await ResolveCategoryIcons(categories.Select(c => c.Slug).ToList());

            // The newest unlocks, for the "what just happened" strip. Entries with no date sort out
            // entirely rather than to the bottom — Temple omits dates for items logged before the
            // player started syncing, and an undated item is not recent, it is merely undated.
            var recent = await (
                from e in dataContext.CharacterCollectionLogEntries.AsNoTracking()
                    .Where(e => e.GameCharacterId == gameCharacterId && e.ObtainedAt != null)
                join item in dataContext.CollectionLogItems.AsNoTracking() on e.ItemId equals item.ItemId
                orderby e.ObtainedAt descending
                select new { e.ItemId, item.Name, e.ObtainedAt })
                .Take(RecentUnlockCount)
                .ToListAsync();

            var recentCategories = await CategoryNamesFor(recent.Select(r => r.ItemId).ToList());

            return new CharacterCollectionLog(
                header.Id,
                header.CharacterName,
                state?.TotalObtained ?? 0,
                // Fall back to our own taxonomy total when no sync has landed, so a never-synced
                // character still shows a sensible denominator instead of "0 of 0".
                state?.TotalAvailable is > 0 ? state.TotalAvailable : categories.Sum(c => c.ItemCount),
                state?.GameMode ?? 0,
                state?.HiscoresRank,
                Freshness(state),
                categories
                    .Select(c =>
                    {
                        icons.TryGetValue(c.Slug, out var icon);
                        return new CollectionLogCategoryProgress(
                            c.Slug, c.DisplayName, c.GroupName,
                            obtained.TryGetValue(c.Slug, out var n) ? n : 0,
                            c.ItemCount,
                            icon.Kind, icon.Name);
                    })
                    .ToList(),
                recent
                    .Select(r => new CollectionLogRecentUnlock(
                        r.ItemId, r.Name,
                        recentCategories.TryGetValue(r.ItemId, out var cat) ? cat : null,
                        r.ObtainedAt!.Value))
                    .ToList());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get collection log for character {CharacterId}", gameCharacterId);
            throw new RepositoryException("Failed to get character collection log", ex);
        }
    }

    public async Task<CollectionLogCategoryView?> GetCategoryItems(int gameCharacterId, string categorySlug)
    {
        try
        {
            var category = await dataContext.CollectionLogCategories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Slug == categorySlug);
            if (category is null) return null;

            // Left join so MISSING items come back too — a collection log that only listed what you
            // already own would be useless.
            var items = await (
                from ci in dataContext.CollectionLogCategoryItems.AsNoTracking().Where(ci => ci.CategorySlug == categorySlug)
                join item in dataContext.CollectionLogItems.AsNoTracking() on ci.ItemId equals item.ItemId
                join e in dataContext.CharacterCollectionLogEntries.AsNoTracking()
                        .Where(e => e.GameCharacterId == gameCharacterId)
                    on ci.ItemId equals e.ItemId into owned
                from e in owned.DefaultIfEmpty()
                orderby ci.SortOrder
                select new CollectionLogItemState(
                    ci.ItemId,
                    item.Name,
                    e != null,
                    e != null ? e.Count : 0,
                    e != null ? e.ObtainedAt : null))
                .ToListAsync();

            var icons = await ResolveCategoryIcons([categorySlug]);
            icons.TryGetValue(categorySlug, out var icon);

            return new CollectionLogCategoryView(
                category.Slug, category.DisplayName, icon.Kind, icon.Name, items);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get category {Category} for character {CharacterId}", categorySlug, gameCharacterId);
            throw new RepositoryException("Failed to get collection log category", ex);
        }
    }

    public async Task<CollectionLogCategoryComparison?> GetCategoryComparison(string categorySlug)
    {
        try
        {
            var category = await dataContext.CollectionLogCategories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Slug == categorySlug);
            if (category is null) return null;

            var items = await (
                from ci in dataContext.CollectionLogCategoryItems.AsNoTracking().Where(ci => ci.CategorySlug == categorySlug)
                join item in dataContext.CollectionLogItems.AsNoTracking() on ci.ItemId equals item.ItemId
                orderby ci.SortOrder
                select new CollectionLogItemState(ci.ItemId, item.Name, false, 0, null))
                .ToListAsync();

            var itemIds = items.Select(i => i.ItemId).ToHashSet();

            var characters = await Visible(dataContext)
                .Join(dataContext.Users.AsNoTracking(), gc => gc.UserId, u => u.Id, (gc, u) => new
                {
                    gc.Id,
                    CharacterName = gc.DisplayName ?? (u.FirstName + " " + u.LastName)
                })
                .ToListAsync();

            // One query for every character's holdings in this category, then grouped in memory —
            // at most (characters × category size) rows, which is a few hundred.
            var held = await dataContext.CharacterCollectionLogEntries.AsNoTracking()
                .Where(e => itemIds.Contains(e.ItemId))
                .Select(e => new { e.GameCharacterId, e.ItemId, e.Count })
                .ToListAsync();

            var byCharacter = held.GroupBy(h => h.GameCharacterId)
                // Count can be 0 upstream for an owned-but-uncounted item; the KEY is what means
                // "has it", so a zero must still produce an entry.
                .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<int, int>)g.ToDictionary(x => x.ItemId, x => x.Count));

            var standings = characters
                .Select(c =>
                {
                    var owned = byCharacter.TryGetValue(c.Id, out var counts)
                        ? counts
                        : new Dictionary<int, int>();
                    return new CollectionLogCategoryStanding(c.Id, c.CharacterName, owned.Count, items.Count, owned);
                })
                .OrderByDescending(s => s.Obtained)
                .ThenBy(s => s.CharacterName)
                .ToList();

            return new CollectionLogCategoryComparison(
                category.Slug, category.DisplayName, category.GroupName, items.Count, items, standings);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to compare collection log category {Category}", categorySlug);
            throw new RepositoryException("Failed to compare collection log category", ex);
        }
    }

    public async Task<CollectionLogItemComparison?> GetItemComparison(int itemId)
    {
        try
        {
            var item = await dataContext.CollectionLogItems.AsNoTracking()
                .FirstOrDefaultAsync(i => i.ItemId == itemId);
            if (item is null) return null;

            var categories = await dataContext.CollectionLogCategoryItems.AsNoTracking()
                .Where(ci => ci.ItemId == itemId)
                .Join(dataContext.CollectionLogCategories.AsNoTracking(), ci => ci.CategorySlug, c => c.Slug, (ci, c) => c.DisplayName)
                .ToListAsync();

            var characters = await Visible(dataContext)
                .Join(dataContext.Users.AsNoTracking(), gc => gc.UserId, u => u.Id, (gc, u) => new
                {
                    gc.Id,
                    CharacterName = gc.DisplayName ?? (u.FirstName + " " + u.LastName)
                })
                .ToListAsync();

            var held = await dataContext.CharacterCollectionLogEntries.AsNoTracking()
                .Where(e => e.ItemId == itemId)
                .ToDictionaryAsync(e => e.GameCharacterId);

            var rollSources = await ResolveRollSources(
                itemId, item.Name, characters.Select(c => c.Id).ToList(), held.Keys.ToHashSet());

            var holders = characters
                .Select(c =>
                {
                    held.TryGetValue(c.Id, out var e);
                    return new CollectionLogItemHolder(
                        c.Id, c.CharacterName,
                        e is not null,
                        e?.Count ?? 0,
                        e?.ObtainedAt,
                        rollSources.TryGetValue(c.Id, out var sources) ? sources : []);
                })
                .OrderByDescending(h => h.Obtained)
                .ThenBy(h => h.ObtainedAt ?? DateTimeOffset.MaxValue)
                .ThenBy(h => h.CharacterName)
                .ToList();

            return new CollectionLogItemComparison(itemId, item.Name, categories, holders);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to compare collection log item {ItemId}", itemId);
            throw new RepositoryException("Failed to compare collection log item", ex);
        }
    }

    public async Task<List<CollectionLogSearchRow>> SearchItems(string? term, int limit)
    {
        try
        {
            var totalCharacters = await Visible(dataContext)
                .Join(dataContext.CharacterCollectionLogStates.AsNoTracking(),
                    gc => gc.Id, s => s.GameCharacterId, (gc, s) => gc.Id)
                .CountAsync();

            var query = dataContext.CollectionLogItems.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(i => EF.Functions.ILike(i.Name, $"%{term.Trim()}%"));

            var items = await query
                .OrderBy(i => i.Name)
                .Take(limit)
                .Select(i => new { i.ItemId, i.Name })
                .ToListAsync();

            if (items.Count == 0) return [];

            var ids = items.Select(i => i.ItemId).ToList();

            var counts = await dataContext.CharacterCollectionLogEntries.AsNoTracking()
                .Where(e => ids.Contains(e.ItemId))
                .GroupBy(e => e.ItemId)
                .Select(g => new { ItemId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ItemId, x => x.Count);

            var categories = (await dataContext.CollectionLogCategoryItems.AsNoTracking()
                .Where(ci => ids.Contains(ci.ItemId))
                .Join(dataContext.CollectionLogCategories.AsNoTracking(),
                    ci => ci.CategorySlug, c => c.Slug, (ci, c) => new { ci.ItemId, c.DisplayName })
                .ToListAsync())
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.DisplayName).ToList());

            return items
                .Select(i => new CollectionLogSearchRow(
                    i.ItemId, i.Name,
                    categories.TryGetValue(i.ItemId, out var cats) ? cats : [],
                    counts.TryGetValue(i.ItemId, out var n) ? n : 0,
                    totalCharacters))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search collection log items");
            throw new RepositoryException("Failed to search collection log items", ex);
        }
    }

    private const int RecentUnlockCount = 12;

    /// <summary>Sources listed against a character who hasn't got the item yet.</summary>
    private const int ChaseSourceCount = 3;

    /// <summary>
    /// Per-character roll counts in the context of one item, from OUR loot data only.
    ///
    /// A character who HAS it gets only the source that actually dropped it to them — an item can
    /// come from several sources, and crediting the wrong one misstates the grind entirely (an
    /// Abyssal whip from an Abyssal demon is not a Sire drop). A character who has NOT got it gets
    /// their biggest few sources among everything known to drop it, because while chasing, where the
    /// rolls have gone is the interesting figure.
    /// </summary>
    private async Task<Dictionary<int, IReadOnlyList<CollectionLogRollSource>>> ResolveRollSources(
        int itemId, string itemName, List<int> characterIds, IReadOnlySet<int> holders)
    {
        var result = new Dictionary<int, IReadOnlyList<CollectionLogRollSource>>();
        if (characterIds.Count == 0) return result;

        // Where this item can come from at all. DropRates is the authority; anywhere it has actually
        // dropped for someone is folded in too, so a source with no stored rate isn't lost.
        var lowered = itemName.ToLowerInvariant();
        var ratedSources = await dataContext.DropRates.AsNoTracking()
            .Where(dr => dr.ItemName.ToLower() == lowered)
            .Select(dr => dr.SourceName)
            .ToListAsync();

        var observedSources = await (
            from d in dataContext.LootDrops.AsNoTracking().Where(d => d.ItemId == itemId)
            join r in dataContext.LootRecords.AsNoTracking() on d.LootRecordId equals r.Id
            select r.SourceName)
            .Distinct()
            .ToListAsync();

        var itemSources = ratedSources.Concat(observedSources).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (itemSources.Count == 0) return result;

        // Which source actually produced it, per character — the only honest attribution.
        var droppedBy = (await (
            from d in dataContext.LootDrops.AsNoTracking().Where(d => d.ItemId == itemId)
            join r in dataContext.LootRecords.AsNoTracking() on d.LootRecordId equals r.Id
            where r.GameCharacterId != null && characterIds.Contains(r.GameCharacterId.Value)
            select new { CharacterId = r.GameCharacterId!.Value, r.SourceName })
            .Distinct()
            .ToListAsync())
            .GroupBy(x => x.CharacterId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase));

        // Roll counts at every relevant source, for every character, in one pass.
        var sourceList = itemSources.ToList();
        var rolls = (await dataContext.LootRecords.AsNoTracking()
                .Where(r => r.GameCharacterId != null
                            && characterIds.Contains(r.GameCharacterId.Value)
                            && sourceList.Contains(r.SourceName))
                .GroupBy(r => new { CharacterId = r.GameCharacterId!.Value, r.SourceName })
                .Select(g => new { g.Key.CharacterId, g.Key.SourceName, Count = g.Count() })
                .ToListAsync())
            .GroupBy(x => x.CharacterId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var characterId in characterIds)
        {
            if (!rolls.TryGetValue(characterId, out var theirs) || theirs.Count == 0) continue;

            // The COLLECTION LOG decides whether they have it — our loot records only decide where
            // it came from. A character with a tracked drop but no collection-log entry (loot data
            // but never synced) would otherwise be shown a green "this dropped it" beside a card
            // that says not obtained.
            droppedBy.TryGetValue(characterId, out var producing);
            if (!holders.Contains(characterId)) producing = null;

            var chosen = producing is { Count: > 0 }
                // Got it: only where it actually came from.
                ? theirs.Where(t => producing.Contains(t.SourceName))
                    .OrderByDescending(t => t.Count)
                    .Select(t => new CollectionLogRollSource(t.SourceName, t.Count, true))
                    .ToList()
                // Still chasing: the biggest few places those rolls have gone.
                : theirs.OrderByDescending(t => t.Count)
                    .Take(ChaseSourceCount)
                    .Select(t => new CollectionLogRollSource(t.SourceName, t.Count, false))
                    .ToList();

            if (chosen.Count > 0) result[characterId] = chosen;
        }

        return result;
    }

    /// <summary>
    /// An icon per category: the boss's own source icon when we hold one, else a representative item
    /// from the category. Only 12 of the 124 categories name a source we have an icon for, so
    /// without the item fallback the overwhelming majority would render blank.
    /// </summary>
    private async Task<Dictionary<string, (CollectionLogIconKind Kind, string? Name)>> ResolveCategoryIcons(
        List<string> slugs)
    {
        var result = new Dictionary<string, (CollectionLogIconKind, string?)>();

        var categories = await dataContext.CollectionLogCategories.AsNoTracking()
            .Where(c => slugs.Contains(c.Slug))
            .Select(c => new { c.Slug, c.DisplayName })
            .ToListAsync();

        var names = categories.Select(c => c.DisplayName).ToList();
        var sourceIcons = (await dataContext.SourceIcons.AsNoTracking()
                .Where(si => names.Contains(si.SourceName))
                .Select(si => si.SourceName)
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The category's first item by Temple's own ordering, which is the in-game log order and so
        // tends to lead with the signature drop or the pet.
        var firstItems = await (
            from ci in dataContext.CollectionLogCategoryItems.AsNoTracking().Where(ci => slugs.Contains(ci.CategorySlug))
            join item in dataContext.CollectionLogItems.AsNoTracking() on ci.ItemId equals item.ItemId
            select new { ci.CategorySlug, ci.SortOrder, item.Name })
            .ToListAsync();

        var leadItem = firstItems
            .GroupBy(x => x.CategorySlug)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).First().Name);

        foreach (var category in categories)
        {
            if (sourceIcons.Contains(category.DisplayName))
                result[category.Slug] = (CollectionLogIconKind.Source, category.DisplayName);
            else if (leadItem.TryGetValue(category.Slug, out var item))
                result[category.Slug] = (CollectionLogIconKind.Item, item);
            else
                result[category.Slug] = (CollectionLogIconKind.None, null);
        }

        return result;
    }

    /// <summary>First category display name per item, for labelling a recent unlock.</summary>
    private async Task<Dictionary<int, string>> CategoryNamesFor(List<int> itemIds)
    {
        if (itemIds.Count == 0) return [];

        var rows = await dataContext.CollectionLogCategoryItems.AsNoTracking()
            .Where(ci => itemIds.Contains(ci.ItemId))
            .Join(dataContext.CollectionLogCategories.AsNoTracking(),
                ci => ci.CategorySlug, c => c.Slug, (ci, c) => new { ci.ItemId, c.DisplayName, c.SortOrder })
            .ToListAsync();

        return rows.GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).First().DisplayName);
    }

    private static CollectionLogFreshness Freshness(CharacterCollectionLogState? state) =>
        state is null
            ? new CollectionLogFreshness(CollectionLogSyncOutcome.Never, null, null, null)
            : new CollectionLogFreshness(state.LastOutcome, state.TempleLastChecked, state.LastSyncedAt, state.LastError);
}
