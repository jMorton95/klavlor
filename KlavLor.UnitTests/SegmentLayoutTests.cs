using KlavLor.Web.Application.Features.Loot.Log.Profile.Svg;

namespace KlavLor.UnitTests;

// The rule these pin: if a slice is worth its own colour in a stacked bar, it must be tall enough to
// carry its own label. That has been broken twice — once by an outlier drop crushing everything else,
// and once by the label floor being applied and then scaled straight back off — so the arithmetic
// lives in its own class now and the guarantee is asserted rather than eyeballed.
public sealed class SegmentLayoutTests
{
    private const double MinSeg = 16;
    private const double Cap = 0.25;

    [Fact]
    public void OneColossalSlice_doesNotStarveTheRestBelowTheLabelFloor()
    {
        // The reported case, from a real character: one drop worth vastly more than everything else
        // in the month. Before the fix the small slices came out around 8px, under the 12px a label
        // needs, so the bar was mostly bare colour.
        var values = new List<long> { 1_300_000_000 };
        for (var i = 0; i < 20; i++) values.Add(2_000_000);

        var px = SegmentLayout.Distribute(values, values.Sum(), barPx: 400, MinSeg, Cap);

        Assert.All(px, h => Assert.True(h >= MinSeg - 0.01, $"a named slice came out at {h:0.##}px"));
    }

    [Fact]
    public void EverySliceClearsTheFloor_wheneverTheBarIsTallEnoughToHoldThem()
    {
        // The general guarantee, across bar heights and slice counts that a real chart produces.
        foreach (var count in new[] { 2, 5, 12, 20, 28 })
        {
            var barPx = SegmentLayout.HeightNeededFor(count, MinSeg);
            var values = Enumerable.Range(1, count).Select(i => (long)(i * i * 1000)).ToList();

            var px = SegmentLayout.Distribute(values, values.Sum(), barPx, MinSeg, Cap);

            Assert.All(px, h => Assert.True(h >= MinSeg - 0.01,
                $"count={count} barPx={barPx} produced a {h:0.##}px slice"));
        }
    }

    [Fact]
    public void SlicesNeverOverflowTheBar()
    {
        // The floors are paid for out of the tall slices, so the stack still has to fit exactly.
        var values = new List<long> { 900_000_000, 5_000_000, 4_000_000, 3_000_000, 2_000_000, 1_000_000 };
        var barPx = 200;

        var px = SegmentLayout.Distribute(values, values.Sum(), barPx, MinSeg, Cap);

        Assert.True(px.Sum() <= barPx + 0.5, $"stack totalled {px.Sum():0.##}px in a {barPx}px bar");
    }

    [Fact]
    public void TheBiggestSliceIsStillTheTallest()
    {
        // Protecting small slices must not reorder the bar — the visual ranking has to keep matching
        // the values, or the chart is lying about which item dominated.
        var values = new List<long> { 100_000, 50_000, 25_000, 10_000, 5_000 };

        var px = SegmentLayout.Distribute(values, values.Sum(), barPx: 400, MinSeg, maxFraction: null);

        for (var i = 1; i < px.Length; i++)
            Assert.True(px[i - 1] >= px[i] - 0.01, $"slice {i - 1} ({px[i - 1]:0.#}) < slice {i} ({px[i]:0.#})");
    }

    [Fact]
    public void WhenTheBarCannotPhysicallyHoldEverySlice_itStillFitsAndStaysProportional()
    {
        // 30 slices at a 16px floor need 480px; this bar has 100. The guarantee is impossible here, so
        // the fallback squeeze takes over — the point is that it fits and does not produce nonsense.
        var values = Enumerable.Repeat(1_000L, 30).ToList();

        var px = SegmentLayout.Distribute(values, values.Sum(), barPx: 100, MinSeg, Cap);

        Assert.True(px.Sum() <= 100.5);
        Assert.All(px, h => Assert.True(h > 0 && double.IsFinite(h)));
    }

    [Fact]
    public void HeightNeededFor_isTheFloorTimesTheCount()
    {
        Assert.Equal(0, SegmentLayout.HeightNeededFor(0, MinSeg));
        Assert.Equal(16, SegmentLayout.HeightNeededFor(1, MinSeg));
        Assert.Equal(448, SegmentLayout.HeightNeededFor(28, MinSeg));
    }

    [Fact]
    public void DegenerateInputsReturnZeroesRatherThanThrowing()
    {
        Assert.Empty(SegmentLayout.Distribute([], 0, 100, MinSeg, Cap));
        Assert.All(SegmentLayout.Distribute([1, 2], barValue: 0, barPx: 100, MinSeg, Cap),
            h => Assert.Equal(0, h));
        Assert.All(SegmentLayout.Distribute([1, 2], barValue: 3, barPx: 0, MinSeg, Cap),
            h => Assert.Equal(0, h));
    }
}
