namespace KlavLor.Application.Common;

public static class IngestTimezone
{
    public static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static DateTimeOffset FromLocalNaive(DateTime localUnspecified)
    {
        var offset = Zone.GetUtcOffset(localUnspecified);
        return new DateTimeOffset(localUnspecified, offset).ToUniversalTime();
    }

    public static DateTimeOffset ToZoneTime(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, Zone);
}
