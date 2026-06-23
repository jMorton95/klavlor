namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories;

/// <summary>
/// Shared SQL for splitting a character's kills into play "sessions" (gap-and-islands), matching the
/// live feed's <c>LootFeedGrouping</c> rules:
/// <list type="bullet">
///   <item>a gap longer than <c>@gap</c> (16h) starts a new session;</item>
///   <item>an overnight break — a gap of at least <c>@breakGap</c> (6h) that crosses a 06:00
///         Europe/London play-day boundary — starts a new session;</item>
///   <item>a hard duration cap: a session can never span more than <c>@gap</c> (16h) from its first
///         kill, so a continuous run that never pauses still can't form one unbounded session.</item>
/// </list>
/// Pure window functions (no recursion). The hosting query must define an <c>ordered</c> CTE exposing
/// <c>"OccurredAt"</c>, <c>"Id"</c> and <c>prev_at</c> (LAG of <c>"OccurredAt"</c> over the same
/// window), and supply the <c>@gap</c> and <c>@breakGap</c> parameters. Emits CTEs ending in
/// <c>sessioned</c>, which carries a stable <c>session_no</c>.
/// </summary>
internal static class SessionSql
{
    /// <param name="partitionCols">
    /// Comma-separated window partition columns (e.g. <c>"SourceName"</c>), or empty for a
    /// single-source query where every row is already the same source.
    /// </param>
    public static string GapIslandsWithCap(string partitionCols)
    {
        var window = string.IsNullOrEmpty(partitionCols)
            ? """OVER (ORDER BY "OccurredAt", "Id")"""
            : $"""OVER (PARTITION BY {partitionCols} ORDER BY "OccurredAt", "Id")""";
        var startPartition = string.IsNullOrEmpty(partitionCols)
            ? "PARTITION BY gap_no"
            : $"PARTITION BY {partitionCols}, gap_no";

        return $"""
            marked AS (
                SELECT *, CASE WHEN prev_at IS NULL
                                 OR ("OccurredAt" - prev_at) > @gap
                                 OR (("OccurredAt" - prev_at) >= @breakGap
                                     AND date(("OccurredAt" AT TIME ZONE 'Europe/London') - INTERVAL '6 hours')
                                      <> date((prev_at AT TIME ZONE 'Europe/London') - INTERVAL '6 hours'))
                                THEN 1 ELSE 0 END AS gap_new
                FROM ordered
            ),
            gapsess AS (
                SELECT *, SUM(gap_new) {window} AS gap_no
                FROM marked
            ),
            chunked AS (
                -- Hard 16h cap: split every @gap of elapsed time since the gap-session's first kill.
                -- A normal day/overnight session is < 16h so chunk stays 0 and nothing extra splits.
                SELECT *, floor(extract(epoch FROM ("OccurredAt" - MIN("OccurredAt") OVER ({startPartition})))
                                / extract(epoch FROM @gap))::int AS chunk
                FROM gapsess
            ),
            capped AS (
                SELECT *, CASE WHEN gap_new = 1 OR chunk <> LAG(chunk) {window}
                                THEN 1 ELSE 0 END AS new_sess
                FROM chunked
            ),
            sessioned AS (
                SELECT *, SUM(new_sess) {window} AS session_no
                FROM capped
            )
            """;
    }
}
