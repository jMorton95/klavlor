namespace KlavLor.Web.Application.Features.Loot.Log.Profile.Svg;

/// <summary>
/// Picks which keys in a stacked bar chart get their own colour, and which fall into "Other".
/// Shared by the monthly value trend and the monthly rolls trend so the two charts colour and
/// bucket their stacks by identical rules.
/// </summary>
public static class StackPalette
{
    /// <summary>
    /// One palette entry: the segment's fill, and the text colour that is legible on it.
    ///
    /// The two are declared together, on the same line, because they have to change together —
    /// a fill edited without its text colour is exactly how a label becomes unreadable. This
    /// replaces an earlier attempt that put a dark plate behind the text instead: that needed no
    /// per-colour bookkeeping but looked like a black box stamped over the chart.
    /// </summary>
    public sealed record Entry(string Fill, string Text);

    // Distinguishable Tailwind fills, listed literally so Tailwind's content scanner emits the
    // classes (computed names wouldn't be picked up). Order matters: earliest = assigned to the
    // biggest overall contributor = top of the legend.
    //
    // Three bands of the same 17 hues, so the palette runs to 51 before anything has to repeat.
    // The later bands are close relatives of the earlier ones, which is acceptable because a named
    // segment carries its item name inside the block — colour is the secondary cue here, not the
    // only one. No pale (-200/-300) shades: they wash out against the panel background.
    //
    // Text colour is per band AND per mode, because a hue's legibility flips between them: amber-500
    // (light theme) takes dark text while amber-600 does not, and every 400-level dark-theme fill is
    // lighter than its 500-level light-theme counterpart.
    public static readonly Entry[] Palette =
    {
        // Band 1 — light theme 500, dark theme 400. The warm/green half is light in both modes.
        new("bg-amber-500 dark:bg-amber-400", "text-slate-900"),
        new("bg-emerald-500 dark:bg-emerald-400", "text-slate-900"),
        new("bg-cyan-500 dark:bg-cyan-400", "text-slate-900"),
        new("bg-violet-500 dark:bg-violet-400", "text-white"),
        new("bg-rose-500 dark:bg-rose-400", "text-white"),
        new("bg-blue-500 dark:bg-blue-400", "text-white"),
        new("bg-orange-500 dark:bg-orange-400", "text-slate-900"),
        new("bg-pink-500 dark:bg-pink-400", "text-white"),
        new("bg-teal-500 dark:bg-teal-400", "text-slate-900"),
        new("bg-lime-500 dark:bg-lime-400", "text-slate-900"),
        new("bg-red-500 dark:bg-red-400", "text-white"),
        new("bg-yellow-500 dark:bg-yellow-400", "text-slate-900"),
        new("bg-green-500 dark:bg-green-400", "text-slate-900"),
        new("bg-sky-500 dark:bg-sky-400", "text-slate-900"),
        new("bg-indigo-500 dark:bg-indigo-400", "text-white"),
        new("bg-purple-500 dark:bg-purple-400", "text-white"),
        new("bg-fuchsia-500 dark:bg-fuchsia-400", "text-white"),

        // Band 2 — light theme 700, dark theme 600. Dark in both modes throughout.
        new("bg-amber-700 dark:bg-amber-600", "text-white"),
        new("bg-emerald-700 dark:bg-emerald-600", "text-white"),
        new("bg-cyan-700 dark:bg-cyan-600", "text-white"),
        new("bg-violet-700 dark:bg-violet-600", "text-white"),
        new("bg-rose-700 dark:bg-rose-600", "text-white"),
        new("bg-blue-700 dark:bg-blue-600", "text-white"),
        new("bg-orange-700 dark:bg-orange-600", "text-white"),
        new("bg-pink-700 dark:bg-pink-600", "text-white"),
        new("bg-teal-700 dark:bg-teal-600", "text-white"),
        new("bg-lime-700 dark:bg-lime-600", "text-white"),
        new("bg-red-700 dark:bg-red-600", "text-white"),
        new("bg-yellow-700 dark:bg-yellow-600", "text-white"),
        new("bg-green-700 dark:bg-green-600", "text-white"),
        new("bg-sky-700 dark:bg-sky-600", "text-white"),
        new("bg-indigo-700 dark:bg-indigo-600", "text-white"),
        new("bg-purple-700 dark:bg-purple-600", "text-white"),
        new("bg-fuchsia-700 dark:bg-fuchsia-600", "text-white"),

        // Band 3 — light theme 600, dark theme 500. The three brightest hues flip between modes.
        new("bg-amber-600 dark:bg-amber-500", "text-white dark:text-slate-900"),
        new("bg-emerald-600 dark:bg-emerald-500", "text-white"),
        new("bg-cyan-600 dark:bg-cyan-500", "text-white"),
        new("bg-violet-600 dark:bg-violet-500", "text-white"),
        new("bg-rose-600 dark:bg-rose-500", "text-white"),
        new("bg-blue-600 dark:bg-blue-500", "text-white"),
        new("bg-orange-600 dark:bg-orange-500", "text-white"),
        new("bg-pink-600 dark:bg-pink-500", "text-white"),
        new("bg-teal-600 dark:bg-teal-500", "text-white"),
        new("bg-lime-600 dark:bg-lime-500", "text-white dark:text-slate-900"),
        new("bg-red-600 dark:bg-red-500", "text-white"),
        new("bg-yellow-600 dark:bg-yellow-500", "text-white dark:text-slate-900"),
        new("bg-green-600 dark:bg-green-500", "text-white"),
        new("bg-sky-600 dark:bg-sky-500", "text-white"),
        new("bg-indigo-600 dark:bg-indigo-500", "text-white"),
        new("bg-purple-600 dark:bg-purple-500", "text-white"),
        new("bg-fuchsia-600 dark:bg-fuchsia-500", "text-white")
    };

    // Mid-grey both ways: slate-600 on the slate-900 panel read as "background", making the
    // (often large) Other block look like empty chart space.
    public static readonly Entry Other = new("bg-slate-400 dark:bg-slate-500", "text-slate-900 dark:text-white");

    /// <summary>
    /// Chooses the keys to name, in colour-assignment order.
    ///
    /// A purely global top-N left individual bars almost entirely grey: a month whose contributors
    /// all sat outside the overall top-N rendered as one "Other" block, which is exactly the
    /// density complaint. So the selection is the union of two rules — the overall biggest
    /// contributors (<paramref name="globalTopN"/>), plus each bar's own top
    /// <paramref name="minPerBar"/>, which guarantees every bar has something named in it no
    /// matter how it compares to the rest of the chart.
    ///
    /// The global set is taken first, so when the union exceeds the palette the entries that
    /// survive are the ones that matter most across the whole chart. Ties break on the key's
    /// string form, so colours don't shuffle between renders of the same data.
    /// </summary>
    public static List<(TKey Key, Entry Style)> Assign<TKey>(
        IReadOnlyList<IReadOnlyList<(TKey Key, long Value)>> bars,
        int globalTopN,
        int minPerBar)
        where TKey : notnull
    {
        var totals = new Dictionary<TKey, long>();
        foreach (var bar in bars)
        {
            foreach (var (key, value) in bar)
            {
                totals.TryGetValue(key, out var existing);
                totals[key] = existing + value;
            }
        }

        var ordered = new List<TKey>();
        var chosen = new HashSet<TKey>();

        void Take(TKey key)
        {
            if (chosen.Add(key)) ordered.Add(key);
        }

        foreach (var key in totals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key.ToString(), StringComparer.Ordinal)
            .Take(Math.Max(0, globalTopN))
            .Select(kv => kv.Key))
        {
            Take(key);
        }

        if (minPerBar > 0)
        {
            // Per-bar guarantees, ranked by overall size so that when the palette runs out the
            // keys dropped are the ones contributing least across the chart as a whole.
            var perBar = new HashSet<TKey>();
            foreach (var bar in bars)
            {
                foreach (var (key, _) in bar
                    .OrderByDescending(s => s.Value)
                    .ThenBy(s => s.Key.ToString(), StringComparer.Ordinal)
                    .Take(minPerBar))
                {
                    perBar.Add(key);
                }
            }

            foreach (var key in perBar
                .OrderByDescending(k => totals.TryGetValue(k, out var t) ? t : 0)
                .ThenBy(k => k.ToString(), StringComparer.Ordinal))
            {
                Take(key);
            }
        }

        return ordered
            .Take(Palette.Length)
            .Select((key, i) => (key, Palette[i]))
            .ToList();
    }
}
