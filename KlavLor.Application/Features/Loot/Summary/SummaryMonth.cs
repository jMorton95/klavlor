using System.Globalization;
using KlavLor.Application.Common;

namespace KlavLor.Application.Features.Loot.Summary;

/// <summary>
/// One calendar month in Europe/London — the zone every monthly aggregate on the site buckets by
/// (the profile's monthly trend, the activity heatmap), so a kill at 23:30 UTC on 30 September is an
/// October kill here exactly as it is there.
///
/// Travels in URLs as <see cref="Slug"/> (<c>2026-10</c>), so a month's summary is linkable.
/// </summary>
public readonly record struct SummaryMonth(int Year, int Month) : IComparable<SummaryMonth>
{
    public static SummaryMonth Containing(DateTimeOffset instant)
    {
        var local = IngestTimezone.ToZoneTime(instant);
        return new SummaryMonth(local.Year, local.Month);
    }

    /// <summary>
    /// The month a summary opens on when none is asked for: the last COMPLETE month. A recap of a
    /// month two days old is two days of data; the one that just ended is the one worth reading.
    /// The month in progress is still reachable one step forward.
    /// </summary>
    public static SummaryMonth DefaultFor(DateTimeOffset now) => Containing(now).Previous();

    /// <summary>
    /// The month a request asked for. Unparseable falls back to the default and a future month is
    /// clamped to the current one, rather than either failing: the slug arrives off a query string.
    /// </summary>
    public static SummaryMonth Resolve(string? slug, DateTimeOffset now)
    {
        if (!TryParse(slug, out var month)) return DefaultFor(now);
        var current = Containing(now);
        return month > current ? current : month;
    }

    public static bool TryParse(string? slug, out SummaryMonth month)
    {
        month = default;
        if (string.IsNullOrWhiteSpace(slug)) return false;
        if (!DateOnly.TryParseExact(slug.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var first))
            return false;
        month = new SummaryMonth(first.Year, first.Month);
        return true;
    }

    public DateOnly FirstDay => new(Year, Month, 1);
    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);
    public int DayCount => DateTime.DaysInMonth(Year, Month);

    /// <summary>Local midnight on the 1st, as UTC. Inclusive lower bound of the month's window.</summary>
    public DateTimeOffset StartUtc => IngestTimezone.FromLocalNaive(FirstDay.ToDateTime(TimeOnly.MinValue));

    /// <summary>The next month's start. Exclusive upper bound, so no kill falls between two months.</summary>
    public DateTimeOffset EndUtc => Next().StartUtc;

    public SummaryMonth Previous() => From(FirstDay.AddMonths(-1));
    public SummaryMonth Next() => From(FirstDay.AddMonths(1));

    public string Slug => $"{Year:D4}-{Month:D2}";
    public string Name => FirstDay.ToString("MMMM", CultureInfo.InvariantCulture);
    public string ShortName => FirstDay.ToString("MMM", CultureInfo.InvariantCulture);
    public string Label => FirstDay.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public int CompareTo(SummaryMonth other) => Ordinal.CompareTo(other.Ordinal);

    public static bool operator <(SummaryMonth a, SummaryMonth b) => a.Ordinal < b.Ordinal;
    public static bool operator >(SummaryMonth a, SummaryMonth b) => a.Ordinal > b.Ordinal;
    public static bool operator <=(SummaryMonth a, SummaryMonth b) => a.Ordinal <= b.Ordinal;
    public static bool operator >=(SummaryMonth a, SummaryMonth b) => a.Ordinal >= b.Ordinal;

    public override string ToString() => Slug;

    private int Ordinal => Year * 12 + Month - 1;

    private static SummaryMonth From(DateOnly day) => new(day.Year, day.Month);
}
