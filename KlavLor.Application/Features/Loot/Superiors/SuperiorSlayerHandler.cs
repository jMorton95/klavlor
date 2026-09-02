using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.SourceModels;
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
public sealed class SuperiorSlayerHandler(
    ISuperiorSlayerRepository repository,
    SourceLootService sourceLoot,
    IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

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

        var comparison = Assemble(counts, baseKills);
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

    private SuperiorComparison Assemble(
        IReadOnlyList<SuperiorCountRow> counts,
        IReadOnlyList<SuperiorBaseKillRow> baseKills)
    {
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

        if (BuildCharacters(counts).Count == 0) return SuperiorComparison.Empty;

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
                    sourceLoot.SuperiorUniqueChance(monster.SlayerLevel));
            })
            .Where(row => row.TotalKills > 0)
            .ToList();

        // Characters are built LAST, from the finished rows: a character's expected uniques is a
        // sum over the rows, so the weighting has to exist before the column that totals it. Built
        // from `rows` rather than from `counts` for the same reason the rows are filtered - a
        // monster that did not make it onto the table must not contribute to a figure the table is
        // supposed to explain.
        return new SuperiorComparison(BuildCharacters(counts, rows), rows);
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

    /// <param name="rows">
    /// The finished, weighted rows. Null while the caller is only counting heads to decide whether
    /// there is a table at all, in which case the expected-uniques figure is left at zero.
    /// </param>
    private static List<SuperiorCharacterColumn> BuildCharacters(
        IReadOnlyList<SuperiorCountRow> counts,
        IReadOnlyList<SuperiorMonsterRow>? rows = null) =>
        counts
            .Where(row => SuperiorSlayerMonsters.Find(row.SourceKey) is not null)
            .GroupBy(row => row.GameCharacterId)
            .Select(g => new SuperiorCharacterColumn(
                g.Key,
                g.First().CharacterName,
                g.First().UserName,
                g.Sum(row => row.Kills),
                g.Max(row => row.LastKilled),
                ExpectedUniquesFor(g.Key, rows)))
            .OrderByDescending(c => c.TotalKills)
            .ThenBy(c => c.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// One character's unique-table rolls: their kills of each superior, each weighted by that
    /// monster's own chance, summed.
    /// </summary>
    /// <remarks>
    /// The sum is the whole point. A single roster-wide rate would be meaningless here because the
    /// chance is per monster and every player kills a different mix of them - which is exactly the
    /// thing the kill totals hide.
    /// </remarks>
    private static double ExpectedUniquesFor(int characterId, IReadOnlyList<SuperiorMonsterRow>? rows) =>
        rows is null ? 0 : rows.Sum(row => row.KillsFor(characterId) * row.UniqueChance);
}
