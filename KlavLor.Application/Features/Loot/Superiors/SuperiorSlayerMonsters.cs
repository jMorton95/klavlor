namespace KlavLor.Application.Features.Loot.Superiors;

/// <summary>
/// One superior slayer monster: the rare upgraded spawn of an ordinary slayer task monster.
/// </summary>
/// <param name="Name">
/// The in-game NPC name, which is what RuneLite reports and therefore what lands in
/// <c>LootRecords.SourceName</c>. Verified against each monster's wiki infobox <c>name</c>
/// parameter rather than the summary table's display text, because the two disagree on casing for
/// about a third of the list ("Colossal Hydra" against "Colossal hydra").
/// </param>
/// <param name="BaseMonsters">
/// The ordinary monster(s) this one spawns from. Two entries have two: a Cockathrice comes from a
/// Cockatrice or a Moonlight Cockatrice, a Nechryarch from a Nechryael or a Greater Nechryael.
/// Display only - we never match a record against a base monster.
/// </param>
/// <param name="SlayerLevel">The Slayer level the base task requires. The list's sort key.</param>
/// <param name="CombatLevel">The superior's own combat level.</param>
/// <param name="Aliases">
/// Alternate SPELLINGS, not alternate casings - matching is lowercased throughout, so a differently
/// cased name needs nothing here. This exists for the one failure this registry cannot otherwise
/// survive: Jagex renaming a monster, which would silently drop its row's data with no error and no
/// empty result to notice. Empty today; add the old name here if one is ever renamed.
/// </param>
public sealed record SuperiorMonster(
    string Name,
    string[] BaseMonsters,
    int SlayerLevel,
    int CombatLevel,
    string[]? Aliases = null)
{
    public string[] Aliases { get; } = Aliases ?? [];
}

/// <summary>
/// The 38 superior slayer monsters, and the unique table every one of them rolls.
/// </summary>
/// <remarks>
/// WHY THIS IS CODE AND NOT A TABLE. It is reference data that changes only when Jagex ships a game
/// update, which is itself a code change (a new superior needs a row here and nothing else). A table
/// would need a migration, an admin panel and a sync job to hold 38 rows that nobody edits. Same
/// reasoning as the hardcoded raid unique lists in RaidUniqueShareStrategy.
///
/// The wiki summary table has 40 rows but only 38 distinct monsters - Cockathrice and Nechryarch
/// each appear twice, once per base monster. They are one entry here, with both bases listed.
///
/// THE UNIQUE TABLE IS SHARED, AND THAT IS WHY THE PAGE IS ORDERED THE WAY IT IS. Every superior,
/// from the level 5 Crushing hand to the level 95 Colossal Hydra, rolls the same unique table, so a
/// player's kills accumulate across all 38 toward the same prize. The chance of hitting it is
///
///     1 / (200 - (slayerLevel + 55)^2 / 125)
///
/// which improves sharply with the base task's Slayer level - a Colossal Hydra is worth many
/// Crushing hands. That formula is recorded here but deliberately NOT computed anywhere yet: the
/// page shows counts only. When it is wanted it belongs behind SourceLootService with every other
/// rate in the app, never hand-rolled at a call site (see CLAUDE.md, "Luck Maths: One Path Only").
/// </remarks>
public static class SuperiorSlayerMonsters
{
    /// <summary>
    /// Ordered by Slayer level ascending, then name - the natural way to write and maintain a
    /// reference list. The page reads it hardest-first and does that reversal itself, because
    /// display order is the page's decision. Pinned by SuperiorSlayerRegistryTests.
    /// </summary>
    public static IReadOnlyList<SuperiorMonster> All { get; } =
    [
        new("Crushing hand",                ["Crawling Hand"],                       5,  45),
        new("Chasm Crawler",                ["Cave crawler"],                       10,  68),
        new("Screaming banshee",            ["Banshee"],                            15,  70),
        new("Screaming twisted banshee",    ["Twisted Banshee"],                    15, 144),
        new("Giant rockslug",               ["Rockslug"],                           20,  86),
        new("Cockathrice",                  ["Cockatrice", "Moonlight Cockatrice"], 25,  89),
        new("Flaming pyrelord",             ["Pyrefiend"],                          30,  97),
        new("Infernal pyrelord",            ["Pyrelord"],                           30, 134),
        new("Monstrous basilisk",           ["Basilisk"],                           40, 135),
        new("Malevolent Mage",              ["Infernal Mage"],                      45, 162),
        new("Insatiable Bloodveld",         ["Bloodveld"],                          50, 202),
        new("Insatiable mutated Bloodveld", ["Mutated Bloodveld"],                  50, 278),
        new("Dire gryphon",                 ["Gryphon"],                            51, 209),
        new("Vitreous Chilled Jelly",       ["Chilled jelly"],                      52, 241),
        new("Vitreous Jelly",               ["Jelly"],                              52, 206),
        new("Vitreous Warped Jelly",        ["Warped Jelly"],                       52, 241),
        new("Spiked Turoth",                ["Turoth"],                             55, 244),
        new("Mutated Terrorbird",           ["Warped Terrorbird"],                  56, 178),
        new("Mutated Tortoise",             ["Warped Tortoise"],                    56, 247),
        new("Cave abomination",             ["Cave horror"],                        58, 206),
        new("Abhorrent spectre",            ["Aberrant spectre"],                   60, 253),
        new("Basilisk Sentinel",            ["Basilisk Knight"],                    60, 358),
        new("Repugnant spectre",            ["Deviant spectre"],                    60, 335),
        new("Magma strykewyrm",             ["Lava Strykewyrm"],                    62, 249),
        new("Shadow Wyrm",                  ["Wyrm"],                               62, 267),
        new("Choke devil",                  ["Dust devil"],                         65, 264),
        new("King kurask",                  ["Kurask"],                             70, 295),
        new("Blood-starved venator",        ["Venator"],                            74, 246),
        new("Marble gargoyle",              ["Gargoyle"],                           75, 349),
        new("Ancient Custodian",            ["Elder custodian stalker"],            76, 239),
        new("Elder aquanite",               ["Aquanite"],                           78, 305),
        new("Nechryarch",                   ["Nechryael", "Greater Nechryael"],     80, 300),
        new("Guardian Drake",               ["Drake"],                              84, 376),
        new("Greater abyssal demon",        ["Abyssal demon"],                      85, 342),
        new("Night beast",                  ["Dark beast"],                         90, 374),
        new("Dreadborn Araxyte",            ["Araxyte"],                            92, 281),
        new("Nuclear smoke devil",          ["Smoke devil"],                        93, 280),
        new("Colossal Hydra",               ["Hydra"],                              95, 309),
    ];

    private static readonly Dictionary<string, SuperiorMonster> Lookup = BuildLookup();

    /// <summary>
    /// Every monster name and alias, lowercased - the array handed to SQL as the source-name filter.
    /// Lowercased because three vocabularies produce a source name and they disagree on case: the
    /// wiki's article titles, the wiki's summary table, and whatever RuneLite reports for the NPC.
    /// </summary>
    public static IReadOnlyList<string> LoweredNames { get; } =
        Lookup.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Every BASE monster name, lowercased - the filter for the "kills of the ordinary monster"
    /// column. Distinct because two superiors can share nothing but several bases repeat across the
    /// list once the dual-base entries are unrolled.
    /// </summary>
    public static IReadOnlyList<string> LoweredBaseMonsterNames { get; } =
        All.SelectMany(m => m.BaseMonsters)
           .Select(b => b.ToLowerInvariant())
           .Distinct(StringComparer.Ordinal)
           .Order(StringComparer.Ordinal)
           .ToList();

    /// <summary>
    /// Resolves a stored source name to its monster, or null when it isn't a superior.
    /// Case-insensitive by construction - every key is already lowercased.
    /// </summary>
    public static SuperiorMonster? Find(string? sourceName) =>
        sourceName is not null && Lookup.TryGetValue(sourceName.Trim().ToLowerInvariant(), out var m)
            ? m
            : null;

    /// <summary>The canonical display name for a stored source name, or null when it isn't a superior.</summary>
    public static string? Canonical(string? sourceName) => Find(sourceName)?.Name;

    private static Dictionary<string, SuperiorMonster> BuildLookup()
    {
        var lookup = new Dictionary<string, SuperiorMonster>(StringComparer.Ordinal);
        foreach (var monster in All)
        {
            // Add throws on a duplicate rather than silently keeping one of them. A collision here
            // means two monsters claim one name, which would merge two rows of the page into one.
            foreach (var name in monster.Aliases.Prepend(monster.Name))
                lookup.Add(name.ToLowerInvariant(), monster);
        }

        return lookup;
    }
}
