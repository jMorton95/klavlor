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
    ///
    /// In practice <see cref="Text"/> is now always the same dark slate — see the palette note —
    /// but it stays on the record so a future fill can't be added without stating what reads on it.
    /// </summary>
    public sealed record Entry(string Fill, string Text);

    // Distinguishable Tailwind fills, listed literally so Tailwind's content scanner emits the
    // classes (computed names wouldn't be picked up). Order matters: earliest = assigned to the
    // biggest overall contributor.
    //
    // EVERY fill here is bright enough to carry dark text, and every entry therefore uses the same
    // dark slate label. That is the whole design constraint, and it is deliberate: white labels were
    // the single worst thing to read on these charts, so the palette gave up the darker half of its
    // range rather than keep them. There is no `dark:` fill variant either — a bright fill reads as a
    // filled block against both the near-white and the slate-900 panel, so one fill serves both
    // themes and a whole class of light/dark mismatch bugs disappears with it.
    //
    // The cost is variety: dropping the 600/700/800 levels leaves three usable bands of the same 17
    // hues (51 entries) rather than four, and the third band has to reach down to -200 for the eight
    // hues that are still too dark at -500 for dark text. Those pale blocks are the weakest link on
    // the light theme — hence the ring on every segment in HistogramBars, which keeps a block defined
    // when its fill sits close to the panel. Accepted knowingly: every named block also carries its
    // item name, and neither chart draws a legend, so colour is the secondary cue here, not the only
    // one.
    public static readonly Entry[] Palette =
    {
        // Band 1 — 400 across every hue. The most saturated level that still takes dark text, so it
        // goes to the biggest contributors.
        new("bg-amber-400", DarkText),
        new("bg-emerald-400", DarkText),
        new("bg-cyan-400", DarkText),
        new("bg-violet-400", DarkText),
        new("bg-rose-400", DarkText),
        new("bg-blue-400", DarkText),
        new("bg-orange-400", DarkText),
        new("bg-pink-400", DarkText),
        new("bg-teal-400", DarkText),
        new("bg-lime-400", DarkText),
        new("bg-red-400", DarkText),
        new("bg-yellow-400", DarkText),
        new("bg-green-400", DarkText),
        new("bg-sky-400", DarkText),
        new("bg-indigo-400", DarkText),
        new("bg-purple-400", DarkText),
        new("bg-fuchsia-400", DarkText),

        // Band 2 — 300 across every hue. Lighter than band 1 but the same hue sequence, so a segment's
        // family is still recognisable when the chart reaches this deep.
        new("bg-amber-300", DarkText),
        new("bg-emerald-300", DarkText),
        new("bg-cyan-300", DarkText),
        new("bg-violet-300", DarkText),
        new("bg-rose-300", DarkText),
        new("bg-blue-300", DarkText),
        new("bg-orange-300", DarkText),
        new("bg-pink-300", DarkText),
        new("bg-teal-300", DarkText),
        new("bg-lime-300", DarkText),
        new("bg-red-300", DarkText),
        new("bg-yellow-300", DarkText),
        new("bg-green-300", DarkText),
        new("bg-sky-300", DarkText),
        new("bg-indigo-300", DarkText),
        new("bg-purple-300", DarkText),
        new("bg-fuchsia-300", DarkText),

        // Band 3 — split by hue, because 500 is where legibility stops being about the number.
        // The warm and green half is still light enough at 500 for dark text; the blue/purple/red half
        // is not, so those eight drop to 200 instead. Both directions move AWAY from band 1, which is
        // what keeps this band distinguishable from it.
        new("bg-amber-500", DarkText),
        new("bg-emerald-500", DarkText),
        new("bg-cyan-500", DarkText),
        new("bg-violet-200", DarkText),
        new("bg-rose-200", DarkText),
        new("bg-blue-200", DarkText),
        new("bg-orange-500", DarkText),
        new("bg-pink-200", DarkText),
        new("bg-teal-500", DarkText),
        new("bg-lime-500", DarkText),
        new("bg-red-200", DarkText),
        new("bg-yellow-500", DarkText),
        new("bg-green-500", DarkText),
        new("bg-sky-500", DarkText),
        new("bg-indigo-200", DarkText),
        new("bg-purple-200", DarkText),
        new("bg-fuchsia-200", DarkText)
    };

    // The label colour every entry uses. Named rather than repeated so "all dark text" is a single
    // fact in one place instead of 51 copies that could drift apart.
    private const string DarkText = "text-slate-900";

    // Mid-grey both ways: slate-600 on the slate-900 panel read as "background", making the
    // (often large) Other block look like empty chart space. slate-400 is bright enough for the same
    // dark label as everything else, so Other needs no special case either.
    public static readonly Entry Other = new("bg-slate-400", DarkText);

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

        // Past the end of the palette the colours repeat rather than the keys being dropped into
        // "Other". Restricting the palette to bright, dark-text fills cost it a whole band, and the
        // expanded view names more items than the remaining 51 — so the choice is a repeated colour or
        // a grey block, and a repeat is much the lesser harm: every named segment is labelled with its
        // own item name and neither chart draws a legend, so nothing is identified by colour alone.
        // Repeats land 51 apart in overall-size order, which puts them in different bars in practice.
        return ordered
            .Select((key, i) => (key, Palette[i % Palette.Length]))
            .ToList();
    }
}
