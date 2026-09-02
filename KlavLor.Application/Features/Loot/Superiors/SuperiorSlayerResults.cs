namespace KlavLor.Application.Features.Loot.Superiors;

// Read models for the Superior Slayer comparison: counts, dates, and the unique-table weighting
// those counts are worth. The weighting comes from SourceLootService.SuperiorUniqueChance and is
// resolved in the handler, never in the view - a Razor file is a call site like any other, and
// CLAUDE.md's "Luck Maths: One Path Only" applies to it too. There is still no luck figure here:
// nothing on this page compares what was received against what was expected.

/// <summary>The whole page: who is being compared, and one row per superior anyone has killed.</summary>
public sealed record SuperiorComparison(
    IReadOnlyList<SuperiorCharacterColumn> Characters,
    IReadOnlyList<SuperiorMonsterRow> Rows,
    /// <summary>
    /// The ordering the rows are ACTUALLY in, which is not always the one that was asked for: an
    /// unknown character id falls back to Slayer level. Carried here so the view marks the column
    /// the table is really sorted by. Passing the requested sort alongside the rows let the two
    /// disagree - a stale bookmark ordered the table by level while no header said so.
    /// </summary>
    SuperiorSort? AppliedSort = null)
{
    public static SuperiorComparison Empty { get; } = new([], []);

    /// <summary>The applied ordering, defaulted for the empty case.</summary>
    public SuperiorSort Ordering => AppliedSort ?? SuperiorSort.Default;
}

/// <summary>One column of the matrix. Ordered by total superior kills descending.</summary>
public sealed record SuperiorCharacterColumn(
    int GameCharacterId,
    string CharacterName,
    string UserName,
    long TotalKills,
    /// <summary>
    /// The most recent superior this character killed, across every monster. Answers "are they
    /// still doing slayer" - without it a big total says nothing about whether it was earned last
    /// week or three years ago. Null only if they have no superior kills, which cannot happen for a
    /// character that made it into this list.
    /// </summary>
    DateTimeOffset? LastKilled = null,
    /// <summary>
    /// Unique-table rolls this character's kills are worth: the sum, over every superior, of their
    /// kills times that monster's chance. This is what the raw total cannot say. Every superior
    /// rolls the SAME table, so kills only become comparable once weighted by level - 1,141 kills
    /// of mostly high-level superiors is worth far more than 1,141 of Crushing hands, and the
    /// column totals alone put those side by side as if they were equal.
    /// </summary>
    double ExpectedUniques = 0)
{
    /// <summary>
    /// Kills per unique-table roll, averaged over what this character actually killed - the quality
    /// of the grind rather than its size. Two players can be a thousand kills apart on the totals
    /// and closer than that on the prize, or the reverse. Zero when there is nothing to divide.
    /// </summary>
    public double KillsPerUnique => ExpectedUniques > 0 ? TotalKills / ExpectedUniques : 0;
}

/// <summary>
/// How the table is ordered.
/// </summary>
/// <param name="CharacterId">
/// Sort by this character's counts. Null - the default - sorts by Slayer level, hardest first.
/// </param>
/// <param name="Ascending">Flips whichever ordering is in effect.</param>
/// <remarks>
/// Applied in memory over the cached comparison, never in SQL: the sort key is a character id
/// rather than a column name, and there is no query to interpolate it into. That sidesteps the
/// whole sort-column whitelist problem the SQL-backed tables have.
/// </remarks>
public sealed record SuperiorSort(int? CharacterId = null, bool Ascending = false)
{
    public static SuperiorSort Default { get; } = new();

    public bool IsByLevel => CharacterId is null;

    public bool IsBy(int gameCharacterId) => CharacterId == gameCharacterId;
}

/// <summary>
/// One superior monster across the roster.
/// </summary>
/// <remarks>
/// Only emitted when at least one character has killed it. A superior nobody has met is not a gap
/// worth thirty-eight rows of dashes - the table is about the kills that exist.
/// </remarks>
public sealed record SuperiorMonsterRow(
    string Name,
    IReadOnlyList<string> BaseMonsters,
    /// <summary>
    /// The base task's Slayer level. It is the page's default sort key and, through
    /// <see cref="UniqueChance"/>, the reason that ordering is worth having - so the table shows it
    /// rather than leaving the reader to infer why the rows are in the order they are.
    /// </summary>
    int SlayerLevel,
    int CombatLevel,
    /// <summary>GameCharacterId to cell. A missing key means never killed, which renders as a dash.</summary>
    IReadOnlyDictionary<int, SuperiorCell> Cells,
    long TotalKills,
    /// <summary>
    /// GameCharacterId to that character's kills of the ORDINARY monster(s) this one spawns from,
    /// summed across both bases where there are two. Per character rather than a roster total:
    /// superiors only appear while killing the base, so this is the grind each player's superior
    /// count sits on top of, and one shared figure could not say whose grind it was. A missing key
    /// means we hold nothing for them there, which the view shows as nothing rather than "0".
    /// </summary>
    IReadOnlyDictionary<int, long> BaseKills,
    /// <summary>
    /// This monster's chance of rolling the shared unique table, from
    /// <see cref="Application.Features.Loot.SourceModels.SourceLootService.SuperiorUniqueChance"/>.
    /// Carried per row rather than recomputed in the view: the view is a call site.
    /// </summary>
    double UniqueChance = 0)
{
    /// <summary>Kills per unique-table roll at this monster, for display as "1 in N".</summary>
    public double KillsPerUnique => UniqueChance > 0 ? 1 / UniqueChance : 0;

    public SuperiorCell? CellFor(int gameCharacterId) =>
        Cells.TryGetValue(gameCharacterId, out var cell) ? cell : null;

    public long KillsFor(int gameCharacterId) => CellFor(gameCharacterId)?.Kills ?? 0;

    public long BaseKillsFor(int gameCharacterId) =>
        BaseKills.TryGetValue(gameCharacterId, out var kills) ? kills : 0;
}

/// <summary>One character's kills of one superior.</summary>
public sealed record SuperiorCell(long Kills, DateTimeOffset FirstKilled, DateTimeOffset LastKilled);

// Repository-level rows. Kept apart from the view models above because they arrive keyed by the
// LOWERCASED source name straight off the query, before the handler maps them onto the registry.

/// <summary>One (character, base monster) aggregate as the base-kills query returns it.</summary>
public sealed record SuperiorBaseKillRow(int GameCharacterId, string SourceKey, long Kills);

/// <summary>One (character, superior) aggregate as the counts query returns it.</summary>
public sealed record SuperiorCountRow(
    int GameCharacterId,
    string CharacterName,
    string UserName,
    string SourceKey,
    long Kills,
    DateTimeOffset FirstKilled,
    DateTimeOffset LastKilled);
