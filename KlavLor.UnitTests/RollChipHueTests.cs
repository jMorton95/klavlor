using KlavLor.Web.Application.Features.Loot.Feed;

namespace KlavLor.UnitTests;

// One colour per character on the roll ticker. The property that matters is DISTINCTNESS across a
// small clan: a hash of the name would need no memory but can collide, and two of five characters
// sharing a colour defeats the point of colouring them at all.
//
// The assigner is process-wide live memory, so these tests share one map and must not assume they
// own it - every name here is unique to its test, and nothing asserts a specific hue number.
public sealed class RollChipHueTests
{
    [Fact]
    public void Every_character_up_to_the_palette_size_gets_a_distinct_colour()
    {
        var names = Enumerable.Range(0, RollChipHues.Count)
            .Select(i => $"distinct-{Guid.NewGuid():N}-{i}")
            .ToList();

        var hues = names.Select(RollChipHues.ClassFor).ToList();

        Assert.Equal(RollChipHues.Count, hues.Distinct().Count());
    }

    [Fact]
    public void A_character_keeps_its_colour_however_often_it_is_asked_for()
    {
        // The whole reason the map exists: a chip rendered now and one rendered an hour later must
        // agree, or the banner recolours a character mid-scroll.
        var name = $"stable-{Guid.NewGuid():N}";
        var first = RollChipHues.ClassFor(name);

        Assert.All(Enumerable.Range(0, 50), _ => Assert.Equal(first, RollChipHues.ClassFor(name)));
    }

    [Fact]
    public void Case_does_not_claim_a_second_colour()
    {
        // Display names reach us from several vocabularies and disagree on case; a re-cased name is
        // the same player and must not consume another slot.
        var name = $"Case-{Guid.NewGuid():N}";

        Assert.Equal(RollChipHues.ClassFor(name), RollChipHues.ClassFor(name.ToUpperInvariant()));
        Assert.Equal(RollChipHues.ClassFor(name), RollChipHues.ClassFor(name.ToLowerInvariant()));
    }

    [Fact]
    public void A_missing_name_is_a_colour_and_not_a_crash()
    {
        // GameCharacterId is nullable upstream, so a nameless roll is possible; it must render.
        Assert.StartsWith("roll-hue-", RollChipHues.ClassFor(null));
        Assert.Equal(RollChipHues.ClassFor(null), RollChipHues.ClassFor(""));
    }

    [Fact]
    public void Every_assigned_class_has_a_rule_in_the_stylesheet()
    {
        // The class names are computed, so nothing else would catch the palette and the assigner
        // drifting apart - the chip would simply render in the default slate and look unstyled.
        var css = File.ReadAllText(StylesheetPath());

        foreach (var hue in Enumerable.Range(1, RollChipHues.Count))
        {
            Assert.Contains($".roll-hue-{hue} ", css);
            Assert.Contains($":where(.dark, .dark *) .roll-hue-{hue} ", css);
        }

        // ...and no rule beyond what the assigner can produce, which would be a dead colour.
        Assert.DoesNotContain($".roll-hue-{RollChipHues.Count + 1} ", css);
    }

    private static string StylesheetPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "KlavLor.Web")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "KlavLor.Web", "wwwroot", "app.css");
    }
}
