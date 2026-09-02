namespace KlavLor.Application.Features.Loot.Superiors;

// Read models for the Superior Slayer comparison. Counts and dates only.
//
// NO UNIQUE-TABLE WEIGHTING, and this is a domain fact rather than an omission. The wiki formula
// gives a BASE chance per Slayer level, but a Slayer master's bonus moves it substantially and
// nothing in a loot record says which master a kill was on - so a weighted figure would be a
// precise-looking number we cannot stand behind. It was briefly computed here and removed; see
// SuperiorSlayerMonsters for the formula and CLAUDE.md for why it stays uncomputed.

/// <summary>The whole page: who is being compared, and one row per superior anyone has killed.</summary>
public sealed record SuperiorComparison(
    IReadOnlyList<SuperiorCharacterColumn> Characters,
    IReadOnlyList<SuperiorMonsterRow> Rows,
    /// <summary>
    /// The shared weekly axis every row's <see cref="SuperiorMonsterRow.Weeks"/> is aligned to,
    /// oldest first. Shared so two sparklines can be read against each other; a row that padded its
    /// own axis would put the same date in a different place on every line.
    /// </summary>
    IReadOnlyList<DateTimeOffset> WeekStarts,
    /// <summary>
    /// The ordering the rows are ACTUALLY in, which is not always the one that was asked for: an
    /// unknown character id falls back to Slayer level. Carried here so the view marks the column
    /// the table is really sorted by. Passing the requested sort alongside the rows let the two
    /// disagree - a stale bookmark ordered the table by level while no header said so.
    /// </summary>
    SuperiorSort? AppliedSort = null)
{
    public static SuperiorComparison Empty { get; } = new([], [], []);

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
    /// Unique-table items this character has received, rarest first. The one column figure that is
    /// not the kill count in disguise.
    /// </summary>
    IReadOnlyList<SuperiorUniqueDrop>? Uniques = null)
{
    public IReadOnlyList<SuperiorUniqueDrop> UniquesReceived => Uniques ?? [];

    /// <summary>
    /// Kills per unique received. DESCRIPTIVE, never predictive: it divides two numbers we hold,
    /// and says nothing about what the rate should be - which is the claim we cannot make, since a
    /// Slayer master's bonus moves the real chance and no record says which master a kill was on.
    /// </summary>
    public double KillsPerUnique =>
        UniquesReceived.Count > 0 ? (double)TotalKills / UniquesReceived.Count : 0;
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
    /// <summary>The base task's Slayer level. The page's default sort key.</summary>
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
    /// Kills per week, aligned to <see cref="SuperiorComparison.WeekStarts"/> and zero-padded, so
    /// index N is the same week on every row.
    /// </summary>
    IReadOnlyList<int> Weeks,
    /// <summary>
    /// Unique-table items this monster has produced, rarest first. Usually empty: 25 receipts across
    /// 38 monsters, which is what makes the ones that exist worth showing.
    /// </summary>
    IReadOnlyList<SuperiorUniqueDrop> Uniques)
{
    /// <summary>The busiest single week, which each sparkline is scaled against.</summary>
    public int PeakWeek => Weeks.Count == 0 ? 0 : Weeks.Max();

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

/// <summary>
/// One unique-table item received from a superior: what dropped, from which monster, for whom, when.
/// </summary>
/// <remarks>
/// The PAYOFF, and the only thing on this page that is not a restatement of the kill count. A
/// superior count is base kills over a constant; a unique is a rare event with real variance, so it
/// is the one figure here where two players genuinely differ.
/// </remarks>
public sealed record SuperiorUniqueDrop(
    string SourceKey,
    string ItemName,
    int GameCharacterId,
    string CharacterName,
    DateTimeOffset OccurredAt);

/// <summary>One (superior, week) bucket as the activity query returns it.</summary>
public sealed record SuperiorWeekRow(string SourceKey, DateTimeOffset WeekStart, int Kills);

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
