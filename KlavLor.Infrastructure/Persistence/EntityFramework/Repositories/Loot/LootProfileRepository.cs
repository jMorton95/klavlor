using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Ingest.Audit;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using static KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot.LootLogSharedQueries;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

// The character profile's own aggregates - header, windowed stats, activity heatmap, monthly
// trend, personal records, top items - plus bulk deletion of a character's or user's records.
// Split out of LootLogRepository by consumer feature; the queries are unchanged.
internal sealed class LootProfileRepository(
    DataContext dataContext, ILogger<LootProfileRepository> logger, IItemValueOverrideCache itemValues)
    : ILootProfileRepository
{
    public async Task<ProfileHeader?> GetProfileHeader(int characterId)
    {
        try
        {
            var character = await dataContext.GameCharacters
                .AsNoTracking()
                .Where(c => c.Id == characterId)
                .Select(c => new
                {
                    c.Id,
                    c.DisplayName,
                    UserFirst = c.User!.FirstName,
                    UserLast = c.User!.LastName
                })
                .FirstOrDefaultAsync();

            if (character is null) return null;

            var userName = $"{character.UserFirst} {character.UserLast}";

            var agg = await dataContext.LootRecords
                .AsNoTracking()
                .Where(r => r.GameCharacterId == characterId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    FirstAt = (DateTimeOffset?)g.Min(r => r.OccurredAt),
                    LastAt = (DateTimeOffset?)g.Max(r => r.OccurredAt),
                    Kills = (long)g.Count(),
                    Gp = g.Sum(r => r.TotalValue),
                    Sources = g.Select(r => r.SourceName).Distinct().Count()
                })
                .FirstOrDefaultAsync();

            return new ProfileHeader(
                character.Id,
                character.DisplayName ?? userName,
                userName,
                agg?.FirstAt,
                agg?.LastAt,
                agg?.Sources ?? 0,
                agg?.Kills ?? 0,
                agg?.Gp ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get profile header for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get profile header", ex);
        }
    }

    public async Task<WindowStats> GetWindowStats(int characterId, DateTimeOffset? from, DateTimeOffset? to)
    {
        try
        {
            var q = dataContext.LootRecords.AsNoTracking().Where(r => r.GameCharacterId == characterId);
            if (from is not null) q = q.Where(r => r.OccurredAt >= from.Value);
            if (to is not null) q = q.Where(r => r.OccurredAt < to.Value);

            var rowAgg = await q
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Kills = (long)g.Count(),
                    Gp = g.Sum(r => r.TotalValue)
                })
                .FirstOrDefaultAsync();

            var kills = rowAgg?.Kills ?? 0;
            var gp = rowAgg?.Gp ?? 0;

            // Active hours = distinct truncated-hour buckets, scaled by the fraction
            // of each hour a player is realistically active (see ActiveFractionPerHour).
            // Cheap approximation of "time spent earning" without session stitching.
            var activeHours = await GetActiveHours(characterId, from, to);
            var gpPerHour = activeHours > 0 ? (long)(gp / activeHours) : 0;

            var newItems = await GetNewItemsInWindow(characterId, from, to);

            return new WindowStats(kills, gp, gpPerHour, newItems, activeHours);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get window stats for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get window stats", ex);
        }
    }

    // Fraction of each "active" hour a player is realistically earning. An hour bucket
    // containing a kill rarely represents 60 minutes of grinding, so we discount each
    // counted hour to ~45 minutes to keep derived figures (e.g. GP/hr) honest.
    private const double ActiveFractionPerHour = 0.75;

    private async Task<double> GetActiveHours(int characterId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        var sql = """
            SELECT COUNT(DISTINCT date_trunc('hour', "OccurredAt"))::bigint
            FROM "LootRecords"
            WHERE "GameCharacterId" = @cid
              AND (@from IS NULL OR "OccurredAt" >= @from)
              AND (@to IS NULL OR "OccurredAt" < @to)
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
        cmd.Parameters.Add(NullableTimestampParam("@from", from));
        cmd.Parameters.Add(NullableTimestampParam("@to", to));

        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l * ActiveFractionPerHour : 0;
    }

    private async Task<int> GetNewItemsInWindow(int characterId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        var sql = """
            SELECT COUNT(DISTINCT ld."Name")::int
            FROM "LootRecords" lr
            JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
            WHERE lr."GameCharacterId" = @cid
              AND ld."IsFirstTime" = true
              AND (@from IS NULL OR lr."OccurredAt" >= @from)
              AND (@to IS NULL OR lr."OccurredAt" < @to)
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
        cmd.Parameters.Add(NullableTimestampParam("@from", from));
        cmd.Parameters.Add(NullableTimestampParam("@to", to));

        var result = await cmd.ExecuteScalarAsync();
        return result is int i ? i : 0;
    }

    public async Task<List<DayBucket>> GetActivityCalendar(int characterId, DateTimeOffset from, DateTimeOffset to)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // Bucket by Europe/London date so BST/GMT transitions don't split a real
            // day across two cells in the heatmap. Clog count = first-time receipts that
            // day whose item is a genuine collection-log item (same join as GetFirstTimeFeed).
            const string sql = """
                SELECT k.day, k.kills, k.gp, COALESCE(c.clogs, 0) AS clogs
                FROM (
                    SELECT (("OccurredAt" AT TIME ZONE 'Europe/London')::date) AS day,
                           COUNT(*)::int AS kills,
                           SUM("TotalValue")::bigint AS gp
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid
                      AND "OccurredAt" >= @from
                      AND "OccurredAt" < @to
                    GROUP BY 1
                ) k
                LEFT JOIN (
                    SELECT (("OccurredAt" AT TIME ZONE 'Europe/London')::date) AS day,
                           COUNT(*)::int AS clogs
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid
                      AND lr."OccurredAt" >= @from
                      AND lr."OccurredAt" < @to
                      AND ld."IsFirstTime" = true
                      -- Split into two EXISTS rather than one with an OR inside: the OR blocks the
                      -- ItemId PK index and forces a full clog-view scan per drop. Separate EXISTS
                      -- each use an index (ItemId PK; lower(Name) expression index) and the fast
                      -- id path short-circuits for the common case.
                      AND (EXISTS (SELECT 1 FROM "EffectiveCollectionLogItems" cli WHERE cli."ItemId" = ld."ItemId")
                           OR EXISTS (SELECT 1 FROM "EffectiveCollectionLogItems" cli WHERE lower(cli."Name") = lower(ld."Name")))
                    GROUP BY 1
                ) c USING (day)
                ORDER BY k.day
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@from", from));
            cmd.Parameters.Add(new NpgsqlParameter("@to", to));

            var result = new List<DayBucket>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new DayBucket(
                    DateOnly.FromDateTime(reader.GetDateTime(0)),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3)));
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get activity calendar for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get activity calendar", ex);
        }
    }

    public async Task<MonthlyTrend> GetMonthlyTrend(int characterId, DateTimeOffset? from, DateTimeOffset to, string range)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // Bucket by (year, month) of the Europe/London occurrence date, matching
            // GetActivityCalendar's TZ. Clog count = first-time receipts that month whose
            // item is a real collection-log entry. When `from` is null we treat it as
            // unbounded ("all time") and use the earliest record to drive UI bounds.
            const string sql = """
                SELECT k.y, k.m, k.kills, k.gp, COALESCE(c.clogs, 0) AS clogs
                FROM (
                    SELECT EXTRACT(year FROM (("OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS y,
                           EXTRACT(month FROM (("OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS m,
                           COUNT(*)::int AS kills,
                           SUM("TotalValue")::bigint AS gp
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid
                      AND (@from IS NULL OR "OccurredAt" >= @from)
                      AND "OccurredAt" < @to
                    GROUP BY 1, 2
                ) k
                LEFT JOIN (
                    SELECT EXTRACT(year FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS y,
                           EXTRACT(month FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS m,
                           COUNT(*)::int AS clogs
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid
                      AND (@from IS NULL OR lr."OccurredAt" >= @from)
                      AND lr."OccurredAt" < @to
                      AND ld."IsFirstTime" = true
                      -- Split into two EXISTS rather than one with an OR inside: the OR blocks the
                      -- ItemId PK index and forces a full clog-view scan per drop. Separate EXISTS
                      -- each use an index (ItemId PK; lower(Name) expression index) and the fast
                      -- id path short-circuits for the common case.
                      AND (EXISTS (SELECT 1 FROM "EffectiveCollectionLogItems" cli WHERE cli."ItemId" = ld."ItemId")
                           OR EXISTS (SELECT 1 FROM "EffectiveCollectionLogItems" cli WHERE lower(cli."Name") = lower(ld."Name")))
                    GROUP BY 1, 2
                ) c USING (y, m)
                ORDER BY k.y, k.m
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(NullableTimestampParam("@from", from));
            cmd.Parameters.Add(new NpgsqlParameter("@to", to));

            var raw = new List<MonthBucket>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    raw.Add(new MonthBucket(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.GetInt32(2),
                        reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                        reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                        []));
                }
            }

            // Top 60 (item, source) contributors per month, by drop value. The global
            // top ~10 (compact) or ~40 (expanded) stack as named segments in the chart;
            // the rest feed the "Other" segment's expanded tooltip. Separate query keeps
            // the monthly-aggregate plan simple and avoids re-unrolling DropsJson inside
            // its CTE.
            const string segmentsSql = """
                WITH unrolled AS (
                    SELECT EXTRACT(year FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS y,
                           EXTRACT(month FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS m,
                           lr."SourceName" AS source_name,
                           ld."Name" AS item_name,
                           (ld."Quantity"::bigint * ld."Price"::bigint) AS value
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid
                      AND (@from IS NULL OR lr."OccurredAt" >= @from)
                      AND lr."OccurredAt" < @to
                ),
                agg AS (
                    SELECT y, m, source_name, item_name, SUM(value)::bigint AS total
                    FROM unrolled
                    GROUP BY y, m, source_name, item_name
                ),
                ranked AS (
                    SELECT y, m, source_name, item_name, total,
                           ROW_NUMBER() OVER (PARTITION BY y, m ORDER BY total DESC) AS rn
                    FROM agg
                )
                SELECT y, m, item_name, source_name, total
                FROM ranked
                WHERE rn <= 60
                ORDER BY y, m, total DESC
                """;

            var segmentsByMonth = new Dictionary<(int y, int m), List<MonthSegment>>();
            await using (var segCmd = connection.CreateCommand())
            {
                segCmd.CommandText = segmentsSql;
                segCmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                segCmd.Parameters.Add(NullableTimestampParam("@from", from));
                segCmd.Parameters.Add(new NpgsqlParameter("@to", to));

                await using var segReader = await segCmd.ExecuteReaderAsync();
                while (await segReader.ReadAsync())
                {
                    var key = (segReader.GetInt32(0), segReader.GetInt32(1));
                    var item = segReader.GetString(2);
                    var source = segReader.GetString(3);
                    var value = segReader.IsDBNull(4) ? 0 : segReader.GetInt64(4);

                    if (!segmentsByMonth.TryGetValue(key, out var list))
                    {
                        list = [];
                        segmentsByMonth[key] = list;
                    }
                    list.Add(new MonthSegment(item, source, value));
                }
            }

            // Splice segments into the aggregate rows.
            for (var i = 0; i < raw.Count; i++)
            {
                if (segmentsByMonth.TryGetValue((raw[i].Year, raw[i].Month), out var segs))
                {
                    raw[i] = raw[i] with { TopSegments = segs };
                }
            }

            // Resolve actual bounds. "all" with no data → degenerate empty range ending today.
            var nowLondon = IngestTimezone.ToZoneTime(to.AddDays(-1));
            DateOnly fromMonth;
            if (from is not null)
            {
                var fromLondon = IngestTimezone.ToZoneTime(from.Value);
                fromMonth = new DateOnly(fromLondon.Year, fromLondon.Month, 1);
            }
            else if (raw.Count > 0)
            {
                fromMonth = new DateOnly(raw[0].Year, raw[0].Month, 1);
            }
            else
            {
                fromMonth = new DateOnly(nowLondon.Year, nowLondon.Month, 1);
            }
            var toMonth = new DateOnly(nowLondon.Year, nowLondon.Month, 1);

            // Densify: fill missing months with zeros so the bar chart renders a
            // contiguous timeline rather than skipping idle months.
            var byKey = raw.ToDictionary(m => (m.Year, m.Month));
            var dense = new List<MonthBucket>();
            for (var cursor = fromMonth; cursor <= toMonth; cursor = cursor.AddMonths(1))
            {
                dense.Add(byKey.TryGetValue((cursor.Year, cursor.Month), out var b)
                    ? b
                    : new MonthBucket(cursor.Year, cursor.Month, 0, 0, 0, []));
            }

            return new MonthlyTrend(fromMonth, toMonth, range, dense);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get monthly trend for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get monthly trend", ex);
        }
    }

    // How many sources a month's stack can name before the rest collapses into "Other". Generous
    // because a busy month legitimately touches dozens of sources and the chart's whole purpose is
    // showing that composition.
    private const int TopSourcesPerMonth = 40;

    public async Task<MonthlyRollTrend> GetMonthlyRolls(int characterId, DateTimeOffset? from, DateTimeOffset to, string range)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // One row per (month, source) with that pair's roll count — a roll being one LootRecords
            // row, i.e. one turn of the source's drop table. Bucketed on the Europe/London
            // occurrence date, matching every other monthly aggregate on this page.
            //
            // month_rolls is a window SUM over the *unfiltered* partition, so the month total stays
            // correct even though only the top sources survive the rn filter — the difference is
            // what the chart shows as "Other". Ties break on source_name so a month with several
            // equal-count sources keeps a stable order between renders rather than returning
            // whichever the aggregate happened to emit first.
            const string sql = """
                WITH agg AS (
                    SELECT EXTRACT(year FROM (("OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS y,
                           EXTRACT(month FROM (("OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS m,
                           "SourceName" AS source_name,
                           COUNT(*)::int AS rolls,
                           SUM("TotalValue")::bigint AS gp
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid
                      AND (@from IS NULL OR "OccurredAt" >= @from)
                      AND "OccurredAt" < @to
                    GROUP BY 1, 2, 3
                ),
                ranked AS (
                    SELECT y, m, source_name, rolls, gp,
                           SUM(rolls) OVER (PARTITION BY y, m)::int AS month_rolls,
                           SUM(gp) OVER (PARTITION BY y, m)::bigint AS month_gp,
                           ROW_NUMBER() OVER (PARTITION BY y, m ORDER BY rolls DESC, source_name) AS rn
                    FROM agg
                )
                SELECT y, m, source_name, rolls, gp, month_rolls, month_gp
                FROM ranked
                WHERE rn <= @topSources
                ORDER BY y, m, rolls DESC, source_name
                """;

            var segmentsByMonth = new Dictionary<(int y, int m), List<RollSourceSegment>>();
            var totalsByMonth = new Dictionary<(int y, int m), (int Rolls, long Gp)>();

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                cmd.Parameters.Add(NullableTimestampParam("@from", from));
                cmd.Parameters.Add(new NpgsqlParameter("@to", to));
                cmd.Parameters.Add(new NpgsqlParameter("@topSources", TopSourcesPerMonth));

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var key = (reader.GetInt32(0), reader.GetInt32(1));
                    if (!segmentsByMonth.TryGetValue(key, out var list))
                    {
                        list = [];
                        segmentsByMonth[key] = list;
                    }
                    list.Add(new RollSourceSegment(
                        reader.GetString(2),
                        reader.GetInt32(3),
                        reader.IsDBNull(4) ? 0 : reader.GetInt64(4)));
                    totalsByMonth[key] = (reader.GetInt32(5), reader.IsDBNull(6) ? 0 : reader.GetInt64(6));
                }
            }

            // Bounds and densification mirror GetMonthlyTrend exactly, so the two charts stacked on
            // the character page always show the same months in the same positions.
            var nowLondon = IngestTimezone.ToZoneTime(to.AddDays(-1));
            var active = totalsByMonth.Keys.OrderBy(k => k.y).ThenBy(k => k.m).ToList();
            DateOnly fromMonth;
            if (from is not null)
            {
                var fromLondon = IngestTimezone.ToZoneTime(from.Value);
                fromMonth = new DateOnly(fromLondon.Year, fromLondon.Month, 1);
            }
            else if (active.Count > 0)
            {
                fromMonth = new DateOnly(active[0].y, active[0].m, 1);
            }
            else
            {
                fromMonth = new DateOnly(nowLondon.Year, nowLondon.Month, 1);
            }
            var toMonth = new DateOnly(nowLondon.Year, nowLondon.Month, 1);

            var dense = new List<RollMonthBucket>();
            for (var cursor = fromMonth; cursor <= toMonth; cursor = cursor.AddMonths(1))
            {
                var key = (cursor.Year, cursor.Month);
                var totals = totalsByMonth.TryGetValue(key, out var t) ? t : (Rolls: 0, Gp: 0L);
                var segs = segmentsByMonth.TryGetValue(key, out var s)
                    ? (IReadOnlyList<RollSourceSegment>)s
                    : [];
                dense.Add(new RollMonthBucket(cursor.Year, cursor.Month, totals.Rolls, totals.Gp, segs));
            }

            return new MonthlyRollTrend(fromMonth, toMonth, range, dense);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get monthly rolls for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get monthly rolls", ex);
        }
    }

    public async Task<PersonalRecords> GetPersonalRecords(int characterId)
    {
        try
        {
            // Biggest single-kill (covered by IX_LootRecords_GameCharacterId_TotalValue_OccurredAt).
            var topKillRaw = await dataContext.LootRecords
                .AsNoTracking()
                .Where(r => r.GameCharacterId == characterId)
                .OrderByDescending(r => r.TotalValue)
                .Take(1)
                .Select(r => new { r.OccurredAt, r.KillCount, r.TotalValue, r.DropsJson, r.SourceName })
                .FirstOrDefaultAsync();

            LootKillEntry? biggestKill = null;
            string? biggestKillSource = null;
            if (topKillRaw is not null)
            {
                // DropsJson holds the raw RuneLite price; re-price through the admin overrides.
                var drops = itemValues.WithEffectivePrices(
                    JsonSerializer.Deserialize<List<LootDrop>>(topKillRaw.DropsJson) ?? []);
                biggestKill = new LootKillEntry(
                    topKillRaw.OccurredAt,
                    topKillRaw.KillCount,
                    null,
                    topKillRaw.TotalValue,
                    drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime))
                        .OrderByDescending(d => (long)d.Quantity * d.Price)
                        .ToList());
                biggestKillSource = topKillRaw.SourceName;
            }

            // Top KC source — most kills of one source.
            var topKcSourceRaw = await dataContext.LootRecords
                .AsNoTracking()
                .Where(r => r.GameCharacterId == characterId)
                .GroupBy(r => new { r.SourceName, r.SourceType })
                .Select(g => new
                {
                    g.Key.SourceName,
                    g.Key.SourceType,
                    Kills = g.Count(),
                    Gp = g.Sum(r => r.TotalValue)
                })
                .OrderByDescending(g => g.Kills)
                .Take(1)
                .FirstOrDefaultAsync();

            var topSource = topKcSourceRaw is null
                ? null
                : new TopSource(topKcSourceRaw.SourceName, topKcSourceRaw.SourceType, topKcSourceRaw.Kills, topKcSourceRaw.Gp);

            // Biggest day — reuse the activity calendar over all time (cheap row agg).
            DayBucket? biggestDay = null;
            var firstRecord = await dataContext.LootRecords
                .AsNoTracking()
                .Where(r => r.GameCharacterId == characterId)
                .OrderBy(r => r.OccurredAt)
                .Select(r => (DateTimeOffset?)r.OccurredAt)
                .FirstOrDefaultAsync();
            if (firstRecord is not null)
            {
                var calendar = await GetActivityCalendar(characterId, firstRecord.Value, DateTimeOffset.UtcNow.AddDays(1));
                biggestDay = calendar.OrderByDescending(d => d.Gp).FirstOrDefault();
            }

            // Best 1h window — load (OccurredAt, TotalValue) and run O(n) sliding window.
            var events = await dataContext.LootRecords
                .AsNoTracking()
                .Where(r => r.GameCharacterId == characterId)
                .OrderBy(r => r.OccurredAt)
                .Select(r => new { r.OccurredAt, r.TotalValue })
                .ToListAsync();
            BestHour? bestHour = null;
            if (events.Count > 0)
            {
                var inferred = SessionInference.BestRollingWindow(
                    events.Select(e => (e.OccurredAt, e.TotalValue)).ToList(),
                    TimeSpan.FromHours(1));
                if (inferred is { } w)
                    bestHour = new BestHour(w.WindowStart, w.Total, w.Count);
            }

            // Most valuable single item — JSONB unroll.
            BiggestItem? biggestItem = await GetBiggestItem(characterId);

            return new PersonalRecords(biggestKill, biggestKillSource, biggestDay, bestHour, topSource, biggestItem);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get personal records for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get personal records", ex);
        }
    }

    private async Task<BiggestItem?> GetBiggestItem(int characterId)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        const string sql = """
            SELECT ld."Name" AS item_name,
                   ld."Quantity" AS qty,
                   (ld."Quantity"::bigint * ld."Price"::bigint) AS value,
                   lr."SourceName",
                   lr."OccurredAt"
            FROM "LootRecords" lr
            JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
            WHERE lr."GameCharacterId" = @cid
            ORDER BY value DESC NULLS LAST
            LIMIT 1
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new BiggestItem(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task<TopItemsList> GetTopItems(int characterId, int limit)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // Aggregate per item across all sources: total qty, total value (qty*price),
            // distinct sources, earliest receipt, and whether the character ever has an
            // IsFirstTime=true marker for the item.
            const string sql = """
                WITH unrolled AS (
                    SELECT lr."OccurredAt", lr."SourceName",
                           ld."Name" AS item_name,
                           ld."Quantity"::bigint AS qty,
                           (ld."Quantity"::bigint * ld."Price"::bigint) AS value,
                           ld."IsFirstTime" AS first_time
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid
                ),
                -- "mostly from <source>" means most OFTEN, so rank by occurrence count rather
                -- than GP value, with source name as a deterministic final tie-break. See the
                -- matching comment in SearchRepository.SearchDrops: without the tie-break, every
                -- zero-price item tied at 0 and the alphabetically first source always won.
                top_source AS (
                    SELECT item_name, "SourceName",
                           ROW_NUMBER() OVER (
                               PARTITION BY item_name
                               ORDER BY COUNT(*) DESC, SUM(qty) DESC, SUM(value) DESC, "SourceName"
                           ) AS rn
                    FROM unrolled
                    GROUP BY item_name, "SourceName"
                )
                SELECT u.item_name,
                       SUM(u.qty)::bigint   AS total_qty,
                       SUM(u.value)::bigint AS total_value,
                       COUNT(DISTINCT u."SourceName")::int AS source_count,
                       MIN(u."OccurredAt")  AS first_received,
                       bool_or(u.first_time) AS ever_first,
                       (SELECT t."SourceName" FROM top_source t WHERE t.item_name = u.item_name AND t.rn = 1) AS top_source
                FROM unrolled u
                GROUP BY u.item_name
                ORDER BY total_value DESC NULLS LAST
                LIMIT @limit
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));

            var items = new List<TopItem>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new TopItem(
                    ItemName: reader.GetString(0),
                    TotalQuantity: reader.GetInt64(1),
                    TotalValue: reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    SourceCount: reader.GetInt32(3),
                    TopSourceName: reader.IsDBNull(6) ? "" : reader.GetString(6),
                    FirstReceivedAt: reader.GetFieldValue<DateTimeOffset>(4),
                    EverFirstTime: !reader.IsDBNull(5) && reader.GetBoolean(5)));
            }
            return new TopItemsList(items);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get top items for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get top items", ex);
        }
    }

    public async Task DeleteAllForCharacter(int characterId)
    {
        try
        {
            await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId)
                .ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete loot records for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to delete loot records for character", ex);
        }
    }

    public async Task DeleteAllForUser(int userId)
    {
        try
        {
            await dataContext.LootRecords
                .Where(r => r.UserId == userId)
                .ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete loot records for user {UserId}", userId);
            throw new RepositoryException("Failed to delete loot records for user", ex);
        }
    }
}
