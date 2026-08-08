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

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

// Play-session history: a character's kills grouped into sessions by the site-wide gap rules
// (SessionSql / LootFeedGrouping), per source and across every source. Split out of
// LootLogRepository by consumer feature; the queries are unchanged.
internal sealed class LootSessionRepository(
    DataContext dataContext, ILogger<LootSessionRepository> logger, ICollectionLogCache collectionLogCache,
    IItemValueOverrideCache itemValues)
    : ILootSessionRepository
{
    // Groups a character's kills at one source into play "sessions" (same rule as the live feed,
    // LootFeedGrouping): a new session starts on a gap longer than MaxGap (16h) OR on an overnight
    // break — a gap of at least SessionBreakGap (6h) that crosses into a different Europe/London
    // play-day (day boundary shifted to 06:00 via `- INTERVAL '6 hours'`). Gap-and-islands in SQL
    // (no JSONB) so it rides IX_LootRecords_GameCharacterId_SourceName; drops are then aggregated
    // only for the requested page of sessions.
    public async Task<LootSourceSessions> GetSourceSessions(int characterId, string sourceName, int pageNumber, int pageSize)
    {
        try
        {
            var characterName = await dataContext.GameCharacters
                .Where(c => c.Id == characterId)
                .Select(c => c.DisplayName ?? c.User!.FirstName + " " + c.User.LastName)
                .FirstOrDefaultAsync();

            var summary = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName)
                .GroupBy(r => new { r.SourceName, r.SourceType })
                .Select(g => new { g.Key.SourceName, g.Key.SourceType, TotalKills = g.Count(), TotalValue = g.Sum(r => r.TotalValue) })
                .FirstOrDefaultAsync();

            if (summary is null)
                return new LootSourceSessions(sourceName, LootSourceType.Unknown, characterName, 0, 0, [], false, 0);

            // Admin baseline KC for this character/source, added to the counted kill ordinals.
            var baseline = await dataContext.CharacterSourceBaselines
                .Where(b => b.GameCharacterId == characterId && b.SourceName == sourceName)
                .Select(b => (int?)b.BaselineKc)
                .FirstOrDefaultAsync() ?? 0;

            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var skip = (pageNumber - 1) * pageSize;

            // session_no starts at 1 for the oldest kill; rank orders newest-session-first for paging.
            var sessionsSql = $"""
                WITH ordered AS (
                    SELECT "Id", "OccurredAt", "KillCount", "TotalValue",
                           LAG("OccurredAt") OVER (ORDER BY "OccurredAt", "Id") AS prev_at,
                           ROW_NUMBER() OVER (ORDER BY "OccurredAt", "Id") AS kill_ord
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid AND "SourceName" = @src
                ),
                {SessionSql.GapIslandsWithCap("")},
                summ AS (
                    SELECT session_no,
                           MIN("OccurredAt") AS started, MAX("OccurredAt") AS ended,
                           COUNT(*)::int AS kills,
                           MIN("KillCount") AS min_kc, MAX("KillCount") AS max_kc,
                           MIN(kill_ord)::int AS min_ord, MAX(kill_ord)::int AS max_ord,
                           SUM("TotalValue")::bigint AS total_gp
                    FROM sessioned GROUP BY session_no
                ),
                ranked AS (
                    SELECT *, ROW_NUMBER() OVER (ORDER BY started DESC) AS rnk,
                           (COUNT(*) OVER ())::int AS total_sessions
                    FROM summ
                )
                SELECT session_no, started, ended, kills, min_kc, max_kc,
                       min_ord + @baseline AS min_ord, max_ord + @baseline AS max_ord, total_gp, total_sessions
                FROM ranked
                WHERE rnk > @skip AND rnk <= @skip + @take
                ORDER BY started DESC
                """;

            var rows = new List<SessionRow>();
            var totalSessions = 0;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sessionsSql;
                cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                cmd.Parameters.Add(new NpgsqlParameter("@src", sourceName));
                cmd.Parameters.Add(new NpgsqlParameter("@gap", LootFeedGrouping.MaxGap));
                cmd.Parameters.Add(new NpgsqlParameter("@breakGap", LootFeedGrouping.SessionBreakGap));
                cmd.Parameters.Add(new NpgsqlParameter("@skip", skip));
                cmd.Parameters.Add(new NpgsqlParameter("@take", pageSize));
                cmd.Parameters.Add(new NpgsqlParameter("@baseline", baseline));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add(new SessionRow(
                        reader.GetInt64(0),
                        reader.GetFieldValue<DateTimeOffset>(1),
                        reader.GetFieldValue<DateTimeOffset>(2),
                        reader.GetInt32(3),
                        reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        reader.GetInt32(6),
                        reader.GetInt32(7),
                        reader.GetInt64(8)));
                    totalSessions = reader.GetInt32(9);
                }
            }

            // Aggregate drops only for the sessions on this page (JSONB unnest is bounded by
            // the session_no filter). Same gap CTE so session_no lines up with the list above.
            var dropsBySession = new Dictionary<long, List<LootKillDrop>>();
            if (rows.Count > 0)
            {
                var sessionNos = rows.Select(r => r.SessionNo).ToArray();
                var dropsSql = $"""
                    WITH ordered AS (
                        SELECT "Id", "OccurredAt", "DropsJson",
                               LAG("OccurredAt") OVER (ORDER BY "OccurredAt", "Id") AS prev_at
                        FROM "LootRecords"
                        WHERE "GameCharacterId" = @cid AND "SourceName" = @src
                    ),
                    {SessionSql.GapIslandsWithCap("")}
                    SELECT s.session_no,
                           ld."Name" AS name,
                           SUM(ld."Quantity"::bigint) AS qty,
                           SUM(ld."Quantity"::bigint * ld."Price"::bigint) AS val,
                           MAX(ld."Price") AS price,
                           bool_or(ld."IsFirstTime") AS first_time,
                           MAX(ld."ItemId") AS item_id
                    FROM sessioned s
                    JOIN "LootDrops" ld ON ld."LootRecordId" = s."Id"
                    WHERE s.session_no = ANY(@sessionNos)
                    GROUP BY s.session_no, ld."Name"
                    ORDER BY s.session_no, val DESC
                    """;

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = dropsSql;
                cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                cmd.Parameters.Add(new NpgsqlParameter("@src", sourceName));
                cmd.Parameters.Add(new NpgsqlParameter("@gap", LootFeedGrouping.MaxGap));
                cmd.Parameters.Add(new NpgsqlParameter("@breakGap", LootFeedGrouping.SessionBreakGap));
                cmd.Parameters.Add(new NpgsqlParameter("@sessionNos", sessionNos));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var sn = reader.GetInt64(0);
                    if (!dropsBySession.TryGetValue(sn, out var list))
                    {
                        list = [];
                        dropsBySession[sn] = list;
                    }
                    var qty = reader.GetInt64(2);
                    list.Add(new LootKillDrop(
                        reader.GetString(1),
                        (int)Math.Min(qty, int.MaxValue),
                        reader.GetInt32(4),
                        reader.GetBoolean(5),
                        collectionLogCache.IsCollectionLogItem(reader.GetInt32(6), reader.GetString(1))));
                }
            }

            const int topDropsPerSession = 8;
            var sessions = rows.Select(r =>
            {
                var drops = dropsBySession.TryGetValue(r.SessionNo, out var d) ? d : [];
                return new LootSession(
                    (int)r.SessionNo,
                    r.Started,
                    r.Ended,
                    r.Kills,
                    r.MinKc,
                    r.MaxKc,
                    r.MinOrd,
                    r.MaxOrd,
                    r.TotalGp,
                    drops.Take(topDropsPerSession).ToList(),
                    drops.Count);
            }).ToList();

            return new LootSourceSessions(
                summary.SourceName,
                summary.SourceType,
                characterName,
                summary.TotalKills,
                summary.TotalValue,
                sessions,
                skip + rows.Count < totalSessions,
                totalSessions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source sessions for character {CharacterId}, source {Source}", characterId, sourceName);
            throw new RepositoryException("Failed to get source sessions", ex);
        }
    }

    // The individual kills inside one session (identified by its session_no), newest-first.
    // Reuses the same gap CTE so ordinals (kill_ord) and grouping match the session list.
    public async Task<List<LootKillEntry>> GetSessionKills(int characterId, string sourceName, int sessionNo)
    {
        try
        {
            var baseline = await dataContext.CharacterSourceBaselines
                .Where(b => b.GameCharacterId == characterId && b.SourceName == sourceName)
                .Select(b => (int?)b.BaselineKc)
                .FirstOrDefaultAsync() ?? 0;

            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var sql = $"""
                WITH ordered AS (
                    SELECT "Id", "OccurredAt", "KillCount", "TotalValue", "DropsJson",
                           LAG("OccurredAt") OVER (ORDER BY "OccurredAt", "Id") AS prev_at,
                           ROW_NUMBER() OVER (ORDER BY "OccurredAt", "Id") AS kill_ord
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid AND "SourceName" = @src
                ),
                {SessionSql.GapIslandsWithCap("")}
                SELECT "OccurredAt", "KillCount", "TotalValue", "DropsJson", kill_ord + @baseline AS kill_ord
                FROM sessioned
                WHERE session_no = @sessionNo
                ORDER BY "OccurredAt" DESC, kill_ord DESC
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@src", sourceName));
            cmd.Parameters.Add(new NpgsqlParameter("@gap", LootFeedGrouping.MaxGap));
            cmd.Parameters.Add(new NpgsqlParameter("@breakGap", LootFeedGrouping.SessionBreakGap));
            cmd.Parameters.Add(new NpgsqlParameter("@sessionNo", (long)sessionNo));
            cmd.Parameters.Add(new NpgsqlParameter("@baseline", baseline));

            var entries = new List<LootKillEntry>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var json = reader.GetString(3);
                // DropsJson holds the raw RuneLite price; re-price through the admin overrides.
                var drops = itemValues.WithEffectivePrices(
                    JsonSerializer.Deserialize<List<LootDrop>>(json) ?? []);
                entries.Add(new LootKillEntry(
                    reader.GetFieldValue<DateTimeOffset>(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    (int)reader.GetInt64(4),
                    reader.GetInt64(2),
                    drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime, collectionLogCache.IsCollectionLogItem(d.ItemId, d.Name)))
                        .OrderByDescending(d => (long)d.Quantity * d.Price)
                        .ToList()));
            }

            return entries;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get session kills for character {CharacterId}, source {Source}, session {Session}", characterId, sourceName, sessionNo);
            throw new RepositoryException("Failed to get session kills", ex);
        }
    }

    private sealed record SessionRow(
        long SessionNo,
        DateTimeOffset Started,
        DateTimeOffset Ended,
        int Kills,
        int? MinKc,
        int? MaxKc,
        int MinOrd,
        int MaxOrd,
        long TotalGp);

    // A character's play sessions across ALL sources. Gap-and-islands PARTITIONED by source
    // (so each source's runs are independent sessions, matching the live feed's per-source
    // grouping), then every session interleaved newest-first by end time and paged. The
    // per-source session_no lines up with GetSessionKills, so expand reuses that unchanged.
    private const char SessionKeySep = '\u0001';

    public async Task<CharacterSessionHistory> GetCharacterSessions(int characterId, int pageNumber, int pageSize)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var skip = (pageNumber - 1) * pageSize;

            var sessionsSql = $"""
                WITH ordered AS (
                    SELECT "Id", "SourceName", "SourceType", "OccurredAt", "KillCount", "TotalValue",
                           LAG("OccurredAt") OVER (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id") AS prev_at,
                           ROW_NUMBER() OVER (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id") AS kill_ord
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid
                ),
                {SessionSql.GapIslandsWithCap("\"SourceName\"")},
                summ AS (
                    SELECT "SourceName", MIN("SourceType") AS source_type, session_no,
                           MIN("OccurredAt") AS started, MAX("OccurredAt") AS ended,
                           COUNT(*)::int AS kills,
                           MIN("KillCount") AS min_kc, MAX("KillCount") AS max_kc,
                           MIN(kill_ord)::int AS min_ord, MAX(kill_ord)::int AS max_ord,
                           SUM("TotalValue")::bigint AS total_gp
                    FROM sessioned GROUP BY "SourceName", session_no
                ),
                kept AS (
                    -- Hide trivial one-offs: a single kill worth under the floor. Multi-kill
                    -- sessions are real grinds and are always kept regardless of value. Filtering
                    -- here (before ranked) keeps paging, total_sessions and HasMore consistent.
                    SELECT * FROM summ WHERE kills > 1 OR total_gp >= @minValue
                ),
                ranked AS (
                    SELECT *, ROW_NUMBER() OVER (ORDER BY ended DESC) AS rnk,
                           (COUNT(*) OVER ())::int AS total_sessions
                    FROM kept
                )
                SELECT "SourceName", source_type, session_no, started, ended, kills,
                       min_kc, max_kc,
                       min_ord + COALESCE((SELECT b."BaselineKc" FROM "CharacterSourceBaselines" b
                                           WHERE b."GameCharacterId" = @cid AND b."SourceName" = ranked."SourceName"), 0) AS min_ord,
                       max_ord + COALESCE((SELECT b."BaselineKc" FROM "CharacterSourceBaselines" b
                                           WHERE b."GameCharacterId" = @cid AND b."SourceName" = ranked."SourceName"), 0) AS max_ord,
                       total_gp, total_sessions
                FROM ranked
                WHERE rnk > @skip AND rnk <= @skip + @take
                ORDER BY ended DESC
                """;

            var rows = new List<CharacterSessionRow>();
            var totalSessions = 0;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sessionsSql;
                cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                cmd.Parameters.Add(new NpgsqlParameter("@gap", LootFeedGrouping.MaxGap));
                cmd.Parameters.Add(new NpgsqlParameter("@breakGap", LootFeedGrouping.SessionBreakGap));
                cmd.Parameters.Add(new NpgsqlParameter("@minValue", LootFeedGrouping.MinOneOffSessionValue));
                cmd.Parameters.Add(new NpgsqlParameter("@skip", skip));
                cmd.Parameters.Add(new NpgsqlParameter("@take", pageSize));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add(new CharacterSessionRow(
                        reader.GetString(0),
                        Enum.TryParse<LootSourceType>(reader.GetString(1), ignoreCase: true, out var st) ? st : LootSourceType.Unknown,
                        reader.GetInt64(2),
                        reader.GetFieldValue<DateTimeOffset>(3),
                        reader.GetFieldValue<DateTimeOffset>(4),
                        reader.GetInt32(5),
                        reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        reader.IsDBNull(7) ? null : reader.GetInt32(7),
                        reader.GetInt32(8),
                        reader.GetInt32(9),
                        reader.GetInt64(10)));
                    totalSessions = reader.GetInt32(11);
                }
            }

            // Per-(source, session) drop aggregation for just the page's sessions, keyed by
            // "sourcesession_no" so the composite filter is a simple text ANY().
            var dropsByKey = new Dictionary<string, List<LootKillDrop>>();
            if (rows.Count > 0)
            {
                var keys = rows.Select(r => r.SourceName + SessionKeySep + r.SessionNo).ToArray();
                var dropsSql = $"""
                    WITH ordered AS (
                        SELECT "Id", "SourceName", "OccurredAt",
                               LAG("OccurredAt") OVER (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id") AS prev_at
                        FROM "LootRecords"
                        WHERE "GameCharacterId" = @cid
                    ),
                    {SessionSql.GapIslandsWithCap("\"SourceName\"")}
                    SELECT s."SourceName" || @sep || s.session_no::text AS skey,
                           ld."Name" AS name,
                           SUM(ld."Quantity"::bigint) AS qty,
                           SUM(ld."Quantity"::bigint * ld."Price"::bigint) AS val,
                           MAX(ld."Price") AS price,
                           bool_or(ld."IsFirstTime") AS first_time,
                           MAX(ld."ItemId") AS item_id
                    FROM sessioned s
                    JOIN "LootDrops" ld ON ld."LootRecordId" = s."Id"
                    WHERE (s."SourceName" || @sep || s.session_no::text) = ANY(@keys)
                    GROUP BY skey, ld."Name"
                    ORDER BY skey, val DESC
                    """;

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = dropsSql;
                cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                cmd.Parameters.Add(new NpgsqlParameter("@gap", LootFeedGrouping.MaxGap));
                cmd.Parameters.Add(new NpgsqlParameter("@breakGap", LootFeedGrouping.SessionBreakGap));
                cmd.Parameters.Add(new NpgsqlParameter("@sep", SessionKeySep.ToString()));
                cmd.Parameters.Add(new NpgsqlParameter("@keys", keys));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var key = reader.GetString(0);
                    if (!dropsByKey.TryGetValue(key, out var list))
                    {
                        list = [];
                        dropsByKey[key] = list;
                    }
                    var qty = reader.GetInt64(2);
                    list.Add(new LootKillDrop(
                        reader.GetString(1),
                        (int)Math.Min(qty, int.MaxValue),
                        reader.GetInt32(4),
                        reader.GetBoolean(5),
                        collectionLogCache.IsCollectionLogItem(reader.GetInt32(6), reader.GetString(1))));
                }
            }

            const int topDropsPerSession = 8;
            var sessions = rows.Select(r =>
            {
                var drops = dropsByKey.TryGetValue(r.SourceName + SessionKeySep + r.SessionNo, out var d) ? d : [];
                return new CharacterSession(
                    r.SourceName,
                    r.SourceType,
                    new LootSession(
                        (int)r.SessionNo,
                        r.Started,
                        r.Ended,
                        r.Kills,
                        r.MinKc,
                        r.MaxKc,
                        r.MinOrd,
                        r.MaxOrd,
                        r.TotalGp,
                        drops.Take(topDropsPerSession).ToList(),
                        drops.Count));
            }).ToList();

            return new CharacterSessionHistory(sessions, skip + rows.Count < totalSessions, totalSessions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get character sessions for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get character sessions", ex);
        }
    }

    private sealed record CharacterSessionRow(
        string SourceName,
        LootSourceType SourceType,
        long SessionNo,
        DateTimeOffset Started,
        DateTimeOffset Ended,
        int Kills,
        int? MinKc,
        int? MaxKc,
        int MinOrd,
        int MaxOrd,
        long TotalGp);
}
