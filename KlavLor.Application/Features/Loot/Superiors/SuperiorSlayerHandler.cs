using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Superiors;

/// <summary>
/// Backs the Superior Slayer comparison page. Assembles the monster-by-character matrix from two
/// roster-wide reads and the static registry.
/// </summary>
/// <remarks>
/// The whole page is one cache entry, keyed off the shared aggregate generation like the other
/// roster fan-outs (GlobalSourceHandler, CollectionLogHandler), so a loot ingest invalidates it.
/// One entry rather than one per panel because there is nothing a caller can vary - no filters, no
/// sort, no paging.
/// </remarks>
public sealed class SuperiorSlayerHandler(ISuperiorSlayerRepository repository, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How far the activity sparkline reaches back. Six months is long enough to show a task coming
    /// and going and short enough that a week is still a readable slice of it.
    /// </summary>
    public const int ActivityWeeks = 26;

    /// <param name="sort">
    /// Applied AFTER the cache, so every ordering shares one cached read rather than one entry per
    /// column - there are only 38 rows, and reordering them is free next to the two queries.
    /// </param>
    public async Task<SuperiorComparison> Get(SuperiorSort? sort = null)
    {
        var key = $"superiors:{AggregateCacheGeneration.Get(cache)}";
        if (cache.TryGetValue(key, out SuperiorComparison? hit) && hit is not null)
            return Sorted(hit, sort);

        // Sequential, never Task.WhenAll: the scoped DbContext/Npgsql connection is not thread-safe
        // and both of these run raw ADO on it (see CLAUDE.md, "Strict DI Rules").
        var counts = await repository.GetCounts(SuperiorSlayerMonsters.LoweredNames);
        var baseKills = await repository.GetBaseMonsterKills(SuperiorSlayerMonsters.LoweredBaseMonsterNames);
        var activity = await repository.GetWeeklyActivity(SuperiorSlayerMonsters.LoweredNames, ActivityWeeks);
        var uniques = await repository.GetUniqueDrops(
            SuperiorSlayerMonsters.LoweredNames, SuperiorSlayerMonsters.LoweredUniqueTable);

        var comparison = Assemble(counts, baseKills, activity, uniques);
        cache.Set(key, comparison, Ttl);
        return Sorted(comparison, sort);
    }

    /// <summary>
    /// Reorders the rows. The default - and the fallback for a character id that is not on the
    /// board - is Slayer level, hardest first; see Assemble for why that is the resting order.
    /// </summary>
    /// <remarks>
    /// An unknown character id falls back rather than throwing or emptying the table: it arrives
    /// from a query string, so a stale bookmark or a character that has since been hidden is an
    /// ordinary thing to receive, not an error.
    /// </remarks>
    private static SuperiorComparison Sorted(SuperiorComparison comparison, SuperiorSort? sort)
    {
        sort ??= SuperiorSort.Default;

        var byCharacter = sort.CharacterId is { } id
                          && comparison.Characters.Any(c => c.GameCharacterId == id)
            ? id
            : (int?)null;

        // Slayer level is the tie-break under a character sort as well as the default ordering, so
        // rows a character has killed none of still fall into a stable, meaningful order.
        var ordered = byCharacter is { } characterId
            ? sort.Ascending
                ? comparison.Rows.OrderBy(r => r.KillsFor(characterId)).ThenByDescending(r => r.SlayerLevel)
                : comparison.Rows.OrderByDescending(r => r.KillsFor(characterId)).ThenByDescending(r => r.SlayerLevel)
            : sort.Ascending
                ? comparison.Rows.OrderBy(r => r.SlayerLevel).ThenBy(r => r.Name, StringComparer.Ordinal)
                : comparison.Rows.OrderByDescending(r => r.SlayerLevel).ThenBy(r => r.Name, StringComparer.Ordinal);

        // The resolved sort, not the requested one: byCharacter is null when the id was unknown.
        return comparison with
        {
            Rows = ordered.ToList(),
            AppliedSort = new SuperiorSort(byCharacter, sort.Ascending)
        };
    }

    private static SuperiorComparison Assemble(
        IReadOnlyList<SuperiorCountRow> counts,
        IReadOnlyList<SuperiorBaseKillRow> baseKills,
        IReadOnlyList<SuperiorWeekRow> activity,
        IReadOnlyList<SuperiorUniqueDrop> uniques)
    {
        // Rarest first within a monster, so a row that produced several leads with the one worth
        // talking about rather than with whichever landed first.
        var uniquesByMonster = uniques
            .GroupBy(u => u.SourceKey, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SuperiorUniqueDrop>)g
                    .OrderBy(u => SuperiorSlayerMonsters.UniqueRank(u.ItemName))
                    .ThenByDescending(u => u.OccurredAt)
                    .ToList(),
                StringComparer.Ordinal);
        // ONE axis for every row, built from the window rather than from the data. Deriving it from
        // the weeks that happen to have kills would give a different axis on a quiet month and make
        // two sparklines drawn side by side mean different things.
        var axis = WeekAxis();
        var weekIndex = axis.Select((w, i) => (w, i)).ToDictionary(x => x.w, x => x.i);

        var activityByMonster = activity
            .GroupBy(a => a.SourceKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        // base monster name -> character -> kills.
        var baseByMonster = baseKills
            .GroupBy(r => r.SourceKey, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<int, long>)g.ToDictionary(r => r.GameCharacterId, r => r.Kills),
                StringComparer.Ordinal);

        // Group by source key first so an unrecognised name is dropped once, here, rather than being
        // carried through the matrix. A record can only reach this point by matching the registry's
        // own filter, so a miss means the registry changed under a cached query - not a data error.
        var byMonster = counts
            .Select(row => (Row: row, Monster: SuperiorSlayerMonsters.Find(row.SourceKey)))
            .Where(x => x.Monster is not null)
            .GroupBy(x => x.Monster!.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var characters = BuildCharacters(counts, uniques);
        if (characters.Count == 0) return SuperiorComparison.Empty;

        // HIGHEST SLAYER LEVEL FIRST. The registry is stored ascending because that is how a
        // reference list is naturally written and maintained; the page reverses it because the
        // high-level superiors are the interesting ones - they roll the shared unique table far more
        // often - and burying them under thirty rows of Crushing hands puts the answer below the
        // fold. Display order is the page's decision, which is why it is taken here rather than by
        // reordering the registry.
        //
        // ONLY MONSTERS SOMEONE HAS ACTUALLY KILLED. A superior nobody in the clan has ever met says
        // nothing that a missing row does not, and thirty rows of dashes push the real data down.
        var rows = SuperiorSlayerMonsters.All
            .Reverse()
            .Select(monster =>
            {
                var cells = byMonster.TryGetValue(monster.Name, out var monsterRows)
                    ? monsterRows.ToDictionary(
                        x => x.Row.GameCharacterId,
                        x => new SuperiorCell(x.Row.Kills, x.Row.FirstKilled, x.Row.LastKilled))
                    : [];

                return new SuperiorMonsterRow(
                    monster.Name,
                    monster.BaseMonsters,
                    monster.SlayerLevel,
                    monster.CombatLevel,
                    cells,
                    cells.Values.Sum(c => c.Kills),
                    BaseKillsFor(monster, baseByMonster),
                    WeeksFor(monster, activityByMonster, axis.Count, weekIndex),
                    uniquesByMonster.GetValueOrDefault(monster.Name.ToLowerInvariant(), []));
            })
            .Where(row => row.TotalKills > 0)
            .ToList();

        return new SuperiorComparison(characters, rows, axis);
    }

    /// <summary>
    /// Per character, their kills of this superior's base monster(s). Summed over both bases where
    /// there are two: a Cockathrice comes from either a Cockatrice or a Moonlight Cockatrice, and
    /// what the figure reports is the size of the task behind the superior, not which spawn it was.
    /// </summary>
    private static IReadOnlyDictionary<int, long> BaseKillsFor(
        SuperiorMonster monster,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, long>> baseByMonster)
    {
        var totals = new Dictionary<int, long>();
        foreach (var baseMonster in monster.BaseMonsters)
        {
            if (!baseByMonster.TryGetValue(baseMonster.ToLowerInvariant(), out var perCharacter)) continue;
            foreach (var (characterId, kills) in perCharacter)
                totals[characterId] = totals.GetValueOrDefault(characterId) + kills;
        }

        return totals;
    }

    /// <summary>
    /// The trailing window of week starts, oldest first, ending with the week in progress.
    /// </summary>
    private static List<DateTimeOffset> WeekAxis()
    {
        var thisWeek = StartOfWeek(DateTimeOffset.UtcNow);
        return Enumerable.Range(0, ActivityWeeks)
            .Select(i => thisWeek.AddDays(-7 * (ActivityWeeks - 1 - i)))
            .ToList();
    }

    /// Monday-based, matching Postgres date_trunc('week', ...) so the buckets the query returns line
    /// up with the axis built here. They are compared as keys, so a mismatch would silently drop
    /// every bucket and draw 38 empty sparklines.
    private static DateTimeOffset StartOfWeek(DateTimeOffset at)
    {
        var date = at.UtcDateTime.Date;
        var offset = ((int)date.DayOfWeek + 6) % 7;   // Sunday = 0 in .NET, but weeks start Monday
        return new DateTimeOffset(date.AddDays(-offset), TimeSpan.Zero);
    }

    /// <summary>Kills per week for one monster, zero-padded onto the shared axis.</summary>
    private static IReadOnlyList<int> WeeksFor(
        SuperiorMonster monster,
        IReadOnlyDictionary<string, List<SuperiorWeekRow>> activityByMonster,
        int length,
        IReadOnlyDictionary<DateTimeOffset, int> weekIndex)
    {
        var weeks = new int[length];
        if (!activityByMonster.TryGetValue(monster.Name.ToLowerInvariant(), out var buckets)) return weeks;

        foreach (var bucket in buckets)
        {
            // A bucket older than the window is simply dropped: the query trims to the window, but
            // a row on the boundary can still fall outside once the axis is rebuilt per request.
            if (weekIndex.TryGetValue(bucket.WeekStart, out var i)) weeks[i] += bucket.Kills;
        }

        return weeks;
    }

    private static List<SuperiorCharacterColumn> BuildCharacters(
        IReadOnlyList<SuperiorCountRow> counts,
        IReadOnlyList<SuperiorUniqueDrop> uniques)
    {
        var byCharacter = uniques
            .GroupBy(u => u.GameCharacterId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SuperiorUniqueDrop>)g
                    .OrderBy(u => SuperiorSlayerMonsters.UniqueRank(u.ItemName))
                    .ThenByDescending(u => u.OccurredAt)
                    .ToList());

        return counts
            .Where(row => SuperiorSlayerMonsters.Find(row.SourceKey) is not null)
            .GroupBy(row => row.GameCharacterId)
            .Select(g => new SuperiorCharacterColumn(
                g.Key,
                g.First().CharacterName,
                g.First().UserName,
                g.Sum(row => row.Kills),
                g.Max(row => row.LastKilled),
                byCharacter.GetValueOrDefault(g.Key, [])))
            .OrderByDescending(c => c.TotalKills)
            .ThenBy(c => c.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
