namespace KlavLor.Application.Features.Loot.Log;

/// <summary>
/// "Time spent earning", estimated without session stitching: the number of distinct clock hours
/// containing at least one kill, each discounted to the fraction of an hour a player is
/// realistically active. An hour bucket with a kill in it rarely represents sixty minutes of
/// grinding, so counting it whole would inflate every derived figure (GP/hr above all).
///
/// The profile's window stats and the monthly summary both quote hours through here, so the
/// "~38h" on one page and the other are the same estimate rather than two that drift apart.
/// </summary>
public static class ActiveHours
{
    public const double FractionPerHour = 0.75;

    public static double FromHourBuckets(long hourBuckets) => hourBuckets * FractionPerHour;
}
