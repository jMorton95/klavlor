namespace KlavLor.Web.Application.Features.Loot.Log.Profile.Svg;

/// <summary>
/// Distributes a stacked bar's pixel heights across its slices.
///
/// Extracted from HistogramBars because it is pure arithmetic that has now produced two visible
/// bugs — an outlier drop crushing every other slice, and the label floor being scaled away again
/// right after it was applied — and neither was catchable while it lived as a local function inside
/// a Razor block.
/// </summary>
public static class SegmentLayout
{
    /// <summary>
    /// Heights in pixels, one per slice, in the order given.
    ///
    /// Three passes, in order:
    ///
    /// 1. Strictly proportional to value.
    /// 2. <paramref name="maxFraction"/> caps how much of the bar any single slice may take, and
    ///    spreads the excess across the rest by value. A 1.3B drop that is 95% of a month would
    ///    otherwise crush every other slice to a sliver. Water-fill, because redistributing can push
    ///    another slice over the cap. The slice's true value and share are unchanged and still shown
    ///    in its label and tooltip, so nothing is misstated. Skipped when the bar has too few slices
    ///    to absorb the excess.
    /// 3. <paramref name="minSeg"/> floors every slice so it is tall enough to carry its own label,
    ///    and the cost of those floors comes out of the slices that have room above the floor — never
    ///    by shrinking the floored ones again. Uniformly scaling everything down, which is what this
    ///    used to do, undoes the floors it just applied.
    /// </summary>
    public static double[] Distribute(
        IReadOnlyList<long> values, long barValue, int barPx, double minSeg, double? maxFraction)
    {
        var n = values.Count;
        var px = new double[n];
        if (n == 0 || barValue <= 0 || barPx <= 0) return px;

        for (var i = 0; i < n; i++)
            px[i] = (double)values[i] / barValue * barPx;

        if (maxFraction is > 0 and < 1 && n * maxFraction.Value >= 1)
        {
            var cap = maxFraction.Value * barPx;
            var frozen = new bool[n];
            while (true)
            {
                var j = -1;
                var maxOver = cap;
                for (var i = 0; i < n; i++)
                    if (!frozen[i] && px[i] > maxOver) { maxOver = px[i]; j = i; }
                if (j < 0) break;

                var excess = px[j] - cap;
                px[j] = cap;
                frozen[j] = true;

                var sumFree = 0d;
                for (var i = 0; i < n; i++) if (!frozen[i]) sumFree += px[i];
                if (sumFree <= 0) { px[j] += excess; break; }  // nothing left to absorb it
                for (var i = 0; i < n; i++)
                    if (!frozen[i]) px[i] += excess * (px[i] / sumFree);
            }
        }

        for (var i = 0; i < n; i++) px[i] = Math.Max(minSeg, px[i]);

        var total = Sum(px);

        // Water-fill downward. Iterates because taking from the tall slices can bring one of them to
        // the floor, which removes it from the pool the remainder has to come out of.
        for (var pass = 0; pass < 64 && total - barPx > 0.01; pass++)
        {
            var headroom = 0d;
            for (var i = 0; i < n; i++) headroom += Math.Max(0, px[i] - minSeg);
            if (headroom <= 0.01) break;

            var take = Math.Min(total - barPx, headroom);
            for (var i = 0; i < n; i++)
            {
                var spare = Math.Max(0, px[i] - minSeg);
                if (spare > 0) px[i] -= take * (spare / headroom);
            }
            total = Sum(px);
        }

        // Only reachable when the floors alone exceed the bar, i.e. more slices than it can
        // physically hold. Callers raise a bar's height to fit its own slices precisely so this stays
        // unreachable, but a proportional squeeze is the only option left if it ever isn't.
        if (total - barPx > 0.01)
        {
            var scale = barPx / total;
            for (var i = 0; i < n; i++) px[i] *= scale;
        }

        return px;
    }

    /// <summary>
    /// The height a bar needs for every one of its slices to clear <paramref name="minSeg"/>. A
    /// slice worth its own colour is worth its own label, so callers use this as a floor on bar
    /// height — otherwise one outlier period sets the peak and every other bar comes out too short
    /// to label anything.
    /// </summary>
    public static int HeightNeededFor(int sliceCount, double minSeg) =>
        sliceCount <= 0 || minSeg <= 0 ? 0 : (int)Math.Ceiling(sliceCount * minSeg);

    private static double Sum(double[] values)
    {
        var total = 0d;
        foreach (var v in values) total += v;
        return total;
    }
}
