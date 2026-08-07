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
    public void SelectionOutgrowingThePaletteCyclesItRatherThanDroppingKeysOrThrowing()
    {
        // 200 distinct keys, each the sole occupant of its own bar, so the per-bar floor wants to name
        // all of them — far more than the 51 colours available. Keeping them named and repeating a
        // colour beats folding them into a grey "Other" block, since every named segment is labelled
        // with its item name. The part that must not happen is indexing past the end of the palette.
        var bars = Enumerable.Range(0, 200)
            .Select(i => Bar(($"item{i}", 1)))
            .ToList();

        var assigned = StackPalette.Assign(bars, globalTopN: 40, minPerBar: 4);

        Assert.Equal(200, assigned.Count);
        Assert.All(assigned, x => Assert.Contains(x.Style, StackPalette.Palette));
        // The first full turn of the palette is still collision-free, which is what keeps the common
        // case (a chart naming fewer items than there are colours) unambiguous.
        Assert.Equal(
            StackPalette.Palette.Length,
            assigned.Take(StackPalette.Palette.Length).Select(x => x.Style.Fill).Distinct().Count());
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
    public void NoPaletteEntryUsesWhiteLabelText()
    {
        // White labels were the reported problem: on these charts they were consistently the hardest
        // segments to read, whichever fill they sat on. The palette's answer was to give up its darker
        // half entirely so that dark text always works — so a re-introduced white label is not a taste
        // call to re-litigate, it is that decision being undone.
        var offenders = StackPalette.Palette
            .Concat([StackPalette.Other])
            .Where(e => e.Text.Contains("text-white", StringComparison.Ordinal))
            .Select(e => e.Fill)
            .ToList();

        Assert.True(offenders.Count == 0,
            "these entries label with white text: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryFillIsBrightEnoughToCarryDarkText()
    {
        // The other half of the same rule. Dark text only works because every fill is a light shade,
        // so the palette may not reach past 500 — and the eight hues that are already too dark for
        // dark text AT 500 (the blue/purple/red side) are held to 200 in the band that would have
        // used it. Shade number is a sound proxy here in a way it wasn't for white text: white's
        // legibility flips by hue at a given shade, whereas "is this light" tracks the number.
        var tooDarkForAnyHue = StackPalette.Palette
            .Concat([StackPalette.Other])
            .Where(e => Shade(e.Fill) > 500)
            .Select(e => e.Fill)
            .ToList();

        Assert.True(tooDarkForAnyHue.Count == 0,
            "these fills are too dark for a dark label: " + string.Join(", ", tooDarkForAnyHue));

        var darkHues = new[] { "blue", "indigo", "violet", "purple", "fuchsia", "pink", "rose", "red" };
        var borderline = StackPalette.Palette
            .Where(e => Shade(e.Fill) >= 500 && darkHues.Any(h => e.Fill.Contains($"-{h}-", StringComparison.Ordinal)))
            .Select(e => e.Fill)
            .ToList();

        Assert.True(borderline.Count == 0,
            "these hues are too dark at 500 to take a dark label: " + string.Join(", ", borderline));
    }

    [Fact]
    public void NoEntryVariesItsFillByTheme()
    {
        // A bright fill reads as a filled block against the near-white panel and the slate-900 one
        // alike, so one fill serves both themes. Every legibility bug this palette has had came from a
        // light/dark pair drifting apart — a 700 dark-mode fill that vanished into the panel, a hue
        // whose text colour needed to flip between modes — and a single fill makes that unexpressible.
        var themed = StackPalette.Palette
            .Concat([StackPalette.Other])
            .Where(e => e.Fill.Contains("dark:", StringComparison.Ordinal)
                     || e.Text.Contains("dark:", StringComparison.Ordinal))
            .Select(e => e.Fill)
            .ToList();

        Assert.True(themed.Count == 0,
            "these entries still branch on theme: " + string.Join(", ", themed));
    }

    // "bg-amber-400" -> 400.
    private static int Shade(string fill) => int.Parse(fill.Split(' ')[0].Split('-')[^1]);

    [Fact]
    public void PaletteFills_AreAllDistinct()
    {
        // Three bands of the same 17 hues, so a copy-paste slip would silently give two entries the
        // same colour. Assign deliberately repeats colours once it runs past the end of the palette;
        // a duplicate WITHIN the palette is different — it wastes a slot and brings the first
        // collision forward.
        Assert.Equal(
            StackPalette.Palette.Length,
            StackPalette.Palette.Select(e => e.Fill).Distinct().Count());
    }
}
