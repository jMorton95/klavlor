using KlavLor.Web.Application.Features.Loot.Log.Profile.Svg;

namespace KlavLor.UnitTests;

// The monthly trend charts used to pick colours from a purely global top-N, which left any month
// whose contributors sat outside that list rendering as a single grey "Other" block. The per-bar
// floor is the fix, and it's the kind of rule that silently regresses, so it's pinned here.
public sealed class StackPaletteTests
{
    private static IReadOnlyList<(string Key, long Value)> Bar(params (string Key, long Value)[] segments) =>
        segments.ToList();

    [Fact]
    public void GlobalTopN_IsHonoured()
    {
        var bars = new[]
        {
            Bar(("a", 100), ("b", 50), ("c", 10))
        };

        var assigned = StackPalette.Assign(bars, globalTopN: 2, minPerBar: 0);

        Assert.Equal(["a", "b"], assigned.Select(x => x.Key));
    }

    [Fact]
    public void EveryBarGetsSomethingNamed_EvenWhenItsItemsAreGloballyTiny()
    {
        // The regression: month two's only contributor is dwarfed by month one's, so a global top-1
        // names nothing in it. The per-bar floor has to rescue it.
        var bars = new[]
        {
            Bar(("huge", 1_000_000)),
            Bar(("tiny", 5))
        };

        var assigned = StackPalette.Assign(bars, globalTopN: 1, minPerBar: 1);
        var keys = assigned.Select(x => x.Key).ToList();

        Assert.Contains("huge", keys);
        Assert.Contains("tiny", keys);
    }

    [Fact]
    public void PerBarFloor_NamesUpToMinPerBarInEachBar()
    {
        var bars = new[]
        {
            Bar(("a1", 30), ("a2", 20), ("a3", 10)),
            Bar(("b1", 3), ("b2", 2), ("b3", 1))
        };

        var keys = StackPalette.Assign(bars, globalTopN: 0, minPerBar: 2)
            .Select(x => x.Key)
            .ToList();

        Assert.Contains("a1", keys);
        Assert.Contains("a2", keys);
        Assert.Contains("b1", keys);
        Assert.Contains("b2", keys);
        // The third in each bar was outside its own top-2 and nothing else vouched for it.
        Assert.DoesNotContain("a3", keys);
        Assert.DoesNotContain("b3", keys);
    }

    [Fact]
    public void ColoursAreDistinctAndOrderedByOverallContribution()
    {
        var bars = new[]
        {
            Bar(("small", 1), ("big", 100), ("mid", 10))
        };

        var assigned = StackPalette.Assign(bars, globalTopN: 3, minPerBar: 0);

        Assert.Equal(["big", "mid", "small"], assigned.Select(x => x.Key));
        Assert.Equal(assigned.Count, assigned.Select(x => x.Style.Fill).Distinct().Count());
    }

    [Fact]
    public void SelectionNeverExceedsThePalette()
    {
        // 200 distinct keys, each the sole occupant of its own bar, so the per-bar floor wants to
        // name all of them. It must still stop at the palette rather than indexing past the end.
        var bars = Enumerable.Range(0, 200)
            .Select(i => Bar(($"item{i}", 1)))
            .ToList();

        var assigned = StackPalette.Assign(bars, globalTopN: 40, minPerBar: 4);

        Assert.Equal(StackPalette.Palette.Length, assigned.Count);
        Assert.Equal(assigned.Count, assigned.Select(x => x.Style.Fill).Distinct().Count());
    }

    [Fact]
    public void EqualValues_ResolveStablyRatherThanByEnumerationOrder()
    {
        // Same data, opposite insertion order. Without the string tie-break the assignment would
        // follow whichever the dictionary happened to enumerate first, so colours would shuffle
        // between renders of identical data.
        var forwards = new[] { Bar(("alpha", 10), ("beta", 10)) };
        var backwards = new[] { Bar(("beta", 10), ("alpha", 10)) };

        Assert.Equal(
            StackPalette.Assign(forwards, globalTopN: 2, minPerBar: 0),
            StackPalette.Assign(backwards, globalTopN: 2, minPerBar: 0));
    }

    [Fact]
    public void NoBars_ReturnsNothing()
    {
        Assert.Empty(StackPalette.Assign(Array.Empty<IReadOnlyList<(string, long)>>(), 10, 3));
    }

    [Fact]
    public void EveryPaletteEntry_PairsAFillWithAnExplicitTextColour()
    {
        // The reason Fill and Text live on one record: a fill added without a legible text colour is
        // how a label silently becomes unreadable on that one shade. Tailwind can't tell us the
        // contrast, so the pairing being present is the part worth enforcing.
        Assert.All(StackPalette.Palette, e =>
        {
            Assert.StartsWith("bg-", e.Fill);
            Assert.Contains("text-", e.Text);
        });
        Assert.StartsWith("bg-", StackPalette.Other.Fill);
        Assert.Contains("text-", StackPalette.Other.Text);
    }

    [Fact]
    public void DarkFillLevelIsNeverDarkerThan600()
    {
        // The fourth band originally used 700-level fills in dark mode. Against the dark panel
        // (slate-900) a 700 is so close to the background that the block reads as empty space with a
        // label floating on it — it looked like the segment had failed to render. 600 is the darkest
        // that still reads as a filled block, so anything beyond it is a bug rather than a taste call.
        var offenders = StackPalette.Palette
            .Select(e => new { e.Fill, Dark = DarkShade(e.Fill) })
            .Where(x => x.Dark >= 700)
            .Select(x => x.Fill)
            .ToList();

        Assert.True(offenders.Count == 0,
            "these entries use a dark-mode fill too close to the panel background: " + string.Join(", ", offenders));
    }

    // Deliberately NOT asserting that light fills carry dark text and vice versa: whether white is
    // legible on a 500-level fill depends on the hue, not the number — white works on violet-500 and
    // fails on lime-500 — so a shade-based rule would give false failures. That pairing is a
    // judgement made per entry in the palette and checked by eye. The rule above is different: it is
    // about a fill's distance from the panel background, which the shade number does determine.

    // "bg-amber-800 dark:bg-amber-300" -> 300, falling back to the light shade when there is no
    // dark variant (such an entry uses the same fill in both themes).
    private static int DarkShade(string fill)
    {
        var token = fill.Split(' ').FirstOrDefault(t => t.StartsWith("dark:bg-"));
        return int.Parse((token ?? fill.Split(' ')[0]).Split('-')[^1]);
    }

    [Fact]
    public void PaletteFills_AreAllDistinct()
    {
        // Three bands of the same hues, so a copy-paste slip would silently give two entries the
        // same colour and make the legend ambiguous.
        Assert.Equal(
            StackPalette.Palette.Length,
            StackPalette.Palette.Select(e => e.Fill).Distinct().Count());
    }
}
