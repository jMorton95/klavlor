using KlavLor.Application.Features.Loot.Superiors;

namespace KlavLor.UnitTests;

// The Superior Slayer registry is hardcoded reference data, so the only way it can be wrong is by a
// hand-editing mistake — a duplicate name, a level out of range, a row inserted in the wrong place.
// Every one of those fails silently on the page: a duplicate merges two rows into one, a misordered
// row lands in the wrong part of a table nobody re-sorts, and a name that no longer matches what
// RuneLite reports simply reads as "nobody has ever killed this".
//
// These tests are what makes editing the list safe.
public sealed class SuperiorSlayerRegistryTests
{
    [Fact]
    public void The_registry_holds_every_superior_slayer_monster()
    {
        // The wiki's summary table has 40 rows for 38 monsters: Cockathrice and Nechryarch each
        // appear twice, once per base monster, and are one entry here with both bases listed.
        Assert.Equal(38, SuperiorSlayerMonsters.All.Count);

        var multiBase = SuperiorSlayerMonsters.All.Where(m => m.BaseMonsters.Length > 1).ToList();
        Assert.Equal(["Cockathrice", "Nechryarch"], multiBase.Select(m => m.Name).Order().ToArray());
    }

    [Fact]
    public void Every_monster_is_named_once()
    {
        var duplicates = SuperiorSlayerMonsters.All
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void No_alias_collides_with_another_monsters_name()
    {
        // An alias exists to survive a rename. One that collides with a DIFFERENT monster's name
        // would route that monster's kills onto the wrong row — so the registry's lookup throws on
        // a duplicate key rather than keeping one of them. This asserts the property directly, in
        // case the lookup is ever made lenient.
        var keys = SuperiorSlayerMonsters.All
            .SelectMany(m => m.Aliases.Prepend(m.Name))
            .Select(n => n.ToLowerInvariant())
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_monster_has_plausible_levels_and_a_base()
    {
        foreach (var monster in SuperiorSlayerMonsters.All)
        {
            Assert.InRange(monster.SlayerLevel, 1, 99);
            Assert.True(monster.CombatLevel > 0, $"{monster.Name} has no combat level");
            Assert.NotEmpty(monster.BaseMonsters);
            Assert.All(monster.BaseMonsters, b => Assert.False(string.IsNullOrWhiteSpace(b)));
            Assert.False(string.IsNullOrWhiteSpace(monster.Name));
        }
    }

    [Fact]
    public void The_registry_is_ordered_by_slayer_level()
    {
        // Stored ASCENDING, which is how a reference list is naturally written and maintained. The
        // page reads it hardest-first and does the reversal itself — display order is the page's
        // decision, and SuperiorSlayerComparisonTests pins that end of it.
        //
        // A new monster appended to the end of this list rather than slotted into place must fail
        // here, not silently appear at the wrong end of the table.
        var expected = SuperiorSlayerMonsters.All
            .OrderBy(m => m.SlayerLevel)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => m.Name)
            .ToList();

        Assert.Equal(expected, SuperiorSlayerMonsters.All.Select(m => m.Name).ToList());
    }

    [Theory]
    // The three vocabularies that produce a source name disagree on case: the wiki article title,
    // the wiki's summary table, and whatever RuneLite reports. All of them must resolve.
    [InlineData("Colossal Hydra", "Colossal Hydra")]
    [InlineData("colossal hydra", "Colossal Hydra")]
    [InlineData("COLOSSAL HYDRA", "Colossal Hydra")]
    [InlineData("  Chasm Crawler  ", "Chasm Crawler")]
    [InlineData("chasm crawler", "Chasm Crawler")]
    [InlineData("malevolent mage", "Malevolent Mage")]
    [InlineData("insatiable mutated bloodveld", "Insatiable mutated Bloodveld")]
    [InlineData("vitreous warped jelly", "Vitreous Warped Jelly")]
    public void A_stored_source_name_resolves_to_its_monster(string stored, string expected)
        => Assert.Equal(expected, SuperiorSlayerMonsters.Canonical(stored));

    [Theory]
    [InlineData("Zulrah")]
    [InlineData("Abyssal demon")]   // the BASE monster is not itself a superior
    [InlineData("Hydra")]
    [InlineData("")]
    [InlineData(null)]
    public void A_source_that_is_not_a_superior_resolves_to_nothing(string? stored)
    {
        Assert.Null(SuperiorSlayerMonsters.Canonical(stored));
        Assert.Null(SuperiorSlayerMonsters.Find(stored));
    }

    [Fact]
    public void Every_monster_name_is_in_the_sql_filter()
    {
        // LoweredNames is what the query filters on. A monster missing from it can never appear on
        // the page however many times it has been killed.
        foreach (var monster in SuperiorSlayerMonsters.All)
            Assert.Contains(monster.Name.ToLowerInvariant(), SuperiorSlayerMonsters.LoweredNames);

        Assert.All(SuperiorSlayerMonsters.LoweredNames, n => Assert.Equal(n.ToLowerInvariant(), n));
    }

    [Fact]
    public void Every_base_monster_is_in_the_base_name_filter()
    {
        // LoweredBaseMonsterNames is what the "kills of the ordinary monster" query filters on. A
        // base missing from it silently drops that figure from its row, with no error to notice.
        foreach (var monster in SuperiorSlayerMonsters.All)
            foreach (var baseMonster in monster.BaseMonsters)
                Assert.Contains(baseMonster.ToLowerInvariant(), SuperiorSlayerMonsters.LoweredBaseMonsterNames);

        Assert.All(SuperiorSlayerMonsters.LoweredBaseMonsterNames, n => Assert.Equal(n.ToLowerInvariant(), n));

        // The list is a deduplicated SET of every base, not one entry per superior: two superiors
        // sharing a base must contribute one filter value, not two.
        Assert.Equal(
            SuperiorSlayerMonsters.All
                .SelectMany(m => m.BaseMonsters)
                .Select(b => b.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count(),
            SuperiorSlayerMonsters.LoweredBaseMonsterNames.Count);

        // A base monster is never itself a superior — the row would then double-count its own kills.
        Assert.All(SuperiorSlayerMonsters.LoweredBaseMonsterNames,
            n => Assert.Null(SuperiorSlayerMonsters.Canonical(n)));
    }
}
