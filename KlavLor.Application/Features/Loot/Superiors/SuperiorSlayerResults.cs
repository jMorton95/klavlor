namespace KlavLor.Application.Features.Loot.Superiors;

// Read models for the Superior Slayer comparison. Counts and dates only: no rate, no luck figure,
// no weighting by Slayer level. See the remarks on SuperiorSlayerMonsters for the drop-rate formula
// and where the maths would belong if it is ever added.

/// <summary>The whole page: who is being compared, and one row per superior anyone has killed.</summary>
public sealed record SuperiorComparison(
    IReadOnlyList<SuperiorCharacterColumn> Characters,
    IReadOnlyList<SuperiorMonsterRow> Rows)
{
    public static SuperiorComparison Empty { get; } = new([], []);
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
    DateTimeOffset? LastKilled = null);

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
    IReadOnlyDictionary<int, long> BaseKills)
{
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
