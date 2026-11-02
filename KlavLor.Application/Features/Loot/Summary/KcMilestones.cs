namespace KlavLor.Application.Features.Loot.Summary;

/// <summary>
/// The round kill counts a monthly summary calls out when a character passes one. Only the highest
/// crossed in the month is reported per source: going 80 → 1,100 is "passed 1,000", not a list of
/// four badges.
/// </summary>
public static class KcMilestones
{
    public static readonly IReadOnlyList<int> Thresholds =
        [50, 100, 250, 500, 1_000, 2_500, 5_000, 10_000, 25_000, 50_000, 100_000];

    /// <summary>The highest threshold t with <c>startKc &lt; t &lt;= endKc</c>, or null.</summary>
    public static int? HighestCrossed(int startKc, int endKc)
    {
        int? highest = null;
        foreach (var threshold in Thresholds)
        {
            if (threshold > endKc) break;
            if (threshold > startKc) highest = threshold;
        }
        return highest;
    }
}
