using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
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
                // Left-joined: a just-released item is exactly what turns up here first, and our
                // own definitions may not have caught up with Temple yet. Dropping it would hide
                // the newest unlock of all — the one the strip exists to show.
                join i in dataContext.CollectionLogItems.AsNoTracking() on e.ItemId equals i.ItemId into named
                from item in named.DefaultIfEmpty()
                orderby e.ObtainedAt descending
                select new { e.ItemId, Name = item != null ? item.Name : "Item " + e.ItemId, e.ObtainedAt })
                .Take(RecentUnlockCount)
                .ToListAsync();

            var recentCategories = await CategoryNamesFor(recent.Select(r => r.ItemId).ToList());
            var recentReceipts = await ResolveFirstReceipts(gameCharacterId, recent.Select(r => r.ItemId).ToList());

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
                        r.ObtainedAt!.Value,
                        recentReceipts.TryGetValue(r.ItemId, out var receipt) ? receipt : null))
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

            // Left join on BOTH sides. Missing items come back because a collection log that only
            // listed what you already own would be useless. And the name is left-joined because the
            // two feeds move independently: Temple can list an item from a fresh game update before
            // our own item definitions have caught up. An inner join dropped those rows silently,
            // so a just-released item was invisible even to a player holding it, and the header
            // count disagreed with the tiles. It now keeps its slot under a placeholder name and
            // fixes itself when the definitions sync.
            var items = await (
                from ci in dataContext.CollectionLogCategoryItems.AsNoTracking().Where(ci => ci.CategorySlug == categorySlug)
                join i in dataContext.CollectionLogItems.AsNoTracking() on ci.ItemId equals i.ItemId into named
                from item in named.DefaultIfEmpty()
                join e in dataContext.CharacterCollectionLogEntries.AsNoTracking()
                        .Where(e => e.GameCharacterId == gameCharacterId)
                    on ci.ItemId equals e.ItemId into owned
                from e in owned.DefaultIfEmpty()
                orderby ci.SortOrder
                select new CollectionLogItemState(
                    ci.ItemId,
                    item != null ? item.Name : "Item " + ci.ItemId,
                    e != null,
                    e != null ? e.Count : 0,
                    e != null ? e.ObtainedAt : null))
                .ToListAsync();

            var icons = await ResolveCategoryIcons([categorySlug]);
            icons.TryGetValue(categorySlug, out var icon);

            // Only obtained items can have a receipt, so only those are looked up.
            var receipts = await ResolveFirstReceipts(
                gameCharacterId, items.Where(i => i.Obtained).Select(i => i.ItemId).ToList());

            var withReceipts = items
                .Select(i => receipts.TryGetValue(i.ItemId, out var receipt) ? i with { FirstReceipt = receipt } : i)
                .ToList();

            return new CollectionLogCategoryView(
                category.Slug, category.DisplayName, icon.Kind, icon.Name, withReceipts);
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
                join i in dataContext.CollectionLogItems.AsNoTracking() on ci.ItemId equals i.ItemId into named
                from item in named.DefaultIfEmpty()
                orderby ci.SortOrder
                select new CollectionLogItemState(
                    ci.ItemId, item != null ? item.Name : "Item " + ci.ItemId, false, 0, null))
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

            // Every character first receipt for every item in the category, in ONE query — the rolls
            // are the point of the comparison, so they are not optional detail fetched per panel.
            // Rows come back only where we actually hold a drop, so an untracked item is absent
            // rather than reported as roll zero.
            var receipts = await ResolveFirstReceipts(
                characters.Select(c => c.Id).ToList(), itemIds.ToList());

            var receiptsByCharacter = receipts
                .GroupBy(kv => kv.Key.CharacterId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyDictionary<int, CollectionLogFirstReceipt>)g.ToDictionary(kv => kv.Key.ItemId, kv => kv.Value));

            // The per-item rolls need a denominator or they say nothing: 40 rolls for a hilt reads
            // very differently against 300 chests than against 3,000. That figure is the character's
            // TOTAL rolls at the source behind the category, resolved from our own loot records.
            var rollSource = await ResolveCategoryRollSource(itemIds.ToList());
            var rolls = rollSource is null
                ? []
                : await ResolveSourceRolls(characters.Select(c => c.Id).ToList(), rollSource);

            var standings = characters
                .Select(c =>
                {
                    var owned = byCharacter.TryGetValue(c.Id, out var counts)
                        ? counts
                        : new Dictionary<int, int>();
                    receiptsByCharacter.TryGetValue(c.Id, out var characterReceipts);
                    return new CollectionLogCategoryStanding(
                        c.Id, c.CharacterName, owned.Count, items.Count, owned, characterReceipts,
                        rolls.TryGetValue(c.Id, out var total) ? total : null);
                })
                .OrderByDescending(s => s.Obtained)
                .ThenBy(s => s.CharacterName)
                .ToList();

            return new CollectionLogCategoryComparison(
                category.Slug, category.DisplayName, category.GroupName, items.Count, items, standings,
                rollSource);
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

    /// <summary>
    /// How far back the "recent unlocks" strip reaches. A hundred rather than a dozen: the strip is
    /// a horizontal scroller, so its length costs layout nothing, and a dozen covered days rather
    /// than months for anyone actively playing - it read as though the character had barely unlocked
    /// anything. The tile icons are lazy-loaded, so the ones off-screen cost no requests either.
    /// </summary>
    /// <remarks>
    /// The cost is flat in this number, not linear in queries: the name, category and first-receipt
    /// lookups all take the whole id list at once (<see cref="CategoryNamesFor"/>,
    /// <see cref="ResolveFirstReceipts"/>), so raising it adds rows to three queries rather than
    /// adding queries.
    /// </remarks>
    private const int RecentUnlockCount = 100;

    /// <summary>
    /// The loot source a category's rolls are counted at: the one our own records most often
    /// credited for items in it.
    /// </summary>
    /// <remarks>
    /// Derived from the data rather than matched on the category's name, because the two
    /// vocabularies do not line up - the log calls it "Barrows Chests" and the loot source is
    /// "Barrows" - and name matching would silently give up on exactly the categories where it is
    /// most wanted. Taking the modal source also survives an item that several sources drop: a Bandos
    /// hilt logged once from a clue does not move the count off Bandos.
    /// </remarks>
    private async Task<string?> ResolveCategoryRollSource(List<int> itemIds)
    {
        if (itemIds.Count == 0) return null;

        return await (
            from ld in dataContext.LootDrops.AsNoTracking().Where(d => itemIds.Contains(d.ItemId))
            join lr in dataContext.LootRecords.AsNoTracking() on ld.LootRecordId equals lr.Id
            group lr by lr.SourceName into bySource
            orderby bySource.Count() descending, bySource.Key
            select bySource.Key)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    ///每 character's total rolls at one source, admin baseline included - the same definition the
    /// character and source pages use, so the figures agree.
    /// </summary>
    private async Task<Dictionary<int, int>> ResolveSourceRolls(List<int> gameCharacterIds, string sourceName)
    {
        var tracked = await dataContext.LootRecords.AsNoTracking()
            .Where(r => r.SourceName == sourceName && r.GameCharacterId != null
                        && gameCharacterIds.Contains(r.GameCharacterId!.Value))
            .GroupBy(r => r.GameCharacterId!.Value)
            .Select(g => new { CharacterId = g.Key, Rolls = g.Count() })
            .ToListAsync();

        var baselines = await dataContext.CharacterSourceBaselines.AsNoTracking()
            .Where(b => b.SourceName == sourceName && gameCharacterIds.Contains(b.GameCharacterId))
            .Select(b => new { b.GameCharacterId, b.BaselineKc })
            .ToListAsync();

        var result = tracked.ToDictionary(t => t.CharacterId, t => t.Rolls);
        foreach (var baseline in baselines)
            result[baseline.GameCharacterId] =
                (result.TryGetValue(baseline.GameCharacterId, out var n) ? n : 0) + baseline.BaselineKc;

        return result;
    }

    /// <summary>
    /// For each item, the source that FIRST dropped it to this character and the roll it landed on.
    ///
    /// This is the one place the two halves of the site meet: the collection log says a character
    /// owns something, our loot records say where it came from and on which roll. An item we never
    /// tracked simply has no entry — it is omitted from the result rather than defaulted, because a
    /// zero would read as "obtained on kill zero".
    ///
    /// The roll number prefers RuneLite's reported kill count and falls back to the record's own
    /// chronological position at that source, plus any admin baseline, exactly as
    /// ILootRecordRepository.GetKillOrdinal does — so the figure agrees with the character page.
    /// </summary>
    private async Task<Dictionary<int, CollectionLogFirstReceipt>> ResolveFirstReceipts(
        int gameCharacterId, List<int> itemIds)
    {
        var byPair = await ResolveFirstReceipts([gameCharacterId], itemIds);
        return byPair.ToDictionary(kv => kv.Key.ItemId, kv => kv.Value);
    }

    /// <summary>
    /// The same attribution for a whole ROSTER at once, keyed by character and item. The comparison
    /// page needs every character&apos;s roll for every item in a category, and that is one query
    /// here rather than one per character.
    /// </summary>
    /// <remarks>
    /// The single-character overload above delegates to this, deliberately: the roll number is a
    /// rule (RuneLite&apos;s reported count, else our own chronological position at that source, plus
    /// any admin baseline) and two copies of it would drift — which is exactly how the character page
    /// and this page would come to disagree about the same drop.
    /// </remarks>
    private async Task<Dictionary<(int CharacterId, int ItemId), CollectionLogFirstReceipt>> ResolveFirstReceipts(
        List<int> gameCharacterIds, List<int> itemIds)
    {
        var result = new Dictionary<(int, int), CollectionLogFirstReceipt>();
        if (itemIds.Count == 0 || gameCharacterIds.Count == 0) return result;

        const string sql = """
            WITH firsts AS (
                SELECT DISTINCT ON (lr."GameCharacterId", ld."ItemId")
                       lr."GameCharacterId" AS character_id,
                       ld."ItemId"      AS item_id,
                       lr."Id"          AS record_id,
                       lr."SourceName"  AS source_name,
                       lr."OccurredAt"  AS occurred_at,
                       lr."KillCount"   AS kill_count
                FROM "LootDrops" ld
                JOIN "LootRecords" lr ON lr."Id" = ld."LootRecordId"
                WHERE lr."GameCharacterId" = ANY(@cids) AND ld."ItemId" = ANY(@items)
                ORDER BY lr."GameCharacterId", ld."ItemId", lr."OccurredAt", lr."Id"
            )
            SELECT f.character_id,
                   f.item_id,
                   f.source_name,
                   f.kill_count,
                   (SELECT COUNT(*)::int
                      FROM "LootRecords" o
                     WHERE o."GameCharacterId" = f.character_id
                       AND o."SourceName" = f.source_name
                       AND (o."OccurredAt" < f.occurred_at
                            OR (o."OccurredAt" = f.occurred_at AND o."Id" <= f.record_id))) AS ordinal,
                   COALESCE((SELECT b."BaselineKc" FROM "CharacterSourceBaselines" b
                              WHERE b."GameCharacterId" = f.character_id AND b."SourceName" = f.source_name), 0) AS baseline
            FROM firsts f
            """;

        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@cids", gameCharacterIds.ToArray()));
        cmd.Parameters.Add(new NpgsqlParameter("@items", itemIds.ToArray()));

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var characterId = reader.GetInt32(0);
            var itemId = reader.GetInt32(1);
            var source = reader.GetString(2);
            var reported = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
            var ordinal = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            var baseline = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);

            // RuneLite reported count when it sent one, else our derived position. Chest sources
            // routinely report none, which is why the fallback exists.
            var kc = reported ?? (ordinal > 0 ? ordinal + baseline : null);
            result[(characterId, itemId)] = new CollectionLogFirstReceipt(source, kc);
        }

        return result;
    }


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
