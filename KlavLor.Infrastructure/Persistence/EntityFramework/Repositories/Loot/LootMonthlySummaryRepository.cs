using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Loot.Summary;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

// Every query here is bounded to one month: [from, to) on OccurredAt for one character, or for each
// of one user's characters. None of them materialise records in memory — they return aggregates and
// capped lists, so a heavy month costs the database more rows to walk but the app no more memory.
internal sealed class LootMonthlySummaryRepository(
    DataContext dataContext, ILogger<LootMonthlySummaryRepository> logger)
    : ILootMonthlySummaryRepository
{
    public async Task<SummaryActivity> GetActivity(int userId, DateTimeOffset from, DateTimeOffset to)
    {
        try
        {
            // One LATERAL aggregate per character, each an index-only range scan of
            // IX_LootRecords_GameCharacterId_TotalValue_OccurredAt (it carries the GP as well as the
            // timestamp). Hour buckets are counted the same way the profile's window stats count
            // them, so ActiveHours means the same thing on both pages.
            const string sql = """
                SELECT gc."Id",
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS name,
                       a.kills, a.hours, a.days, a.gp,
                       (SELECT MIN(f."OccurredAt") FROM "LootRecords" f
                         WHERE f."UserId" = @uid AND f."GameCharacterId" IS NOT NULL) AS first_at
                FROM "GameCharacters" gc
                JOIN "Users" u ON u."Id" = gc."UserId"
                CROSS JOIN LATERAL (
                    SELECT COUNT(*)::int AS kills,
                           COUNT(DISTINCT date_trunc('hour', lr."OccurredAt"))::int AS hours,
                           COUNT(DISTINCT (lr."OccurredAt" AT TIME ZONE 'Europe/London')::date)::int AS days,
                           COALESCE(SUM(lr."TotalValue"), 0)::bigint AS gp
                    FROM "LootRecords" lr
                    WHERE lr."GameCharacterId" = gc."Id"
                      AND lr."OccurredAt" >= @from
                      AND lr."OccurredAt" < @to
                ) a
                WHERE gc."UserId" = @uid
                ORDER BY a.hours DESC, a.kills DESC, a.gp DESC, gc."Id"
                """;

            await using var cmd = await Command(sql);
            cmd.Parameters.Add(new NpgsqlParameter("@uid", userId));
            cmd.Parameters.Add(new NpgsqlParameter("@from", from));
            cmd.Parameters.Add(new NpgsqlParameter("@to", to));

            var characters = new List<SummaryCharacter>();
            DateTimeOffset? firstAt = null;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                characters.Add(new SummaryCharacter(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt64(5)));
                firstAt ??= reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6);
            }
            return new SummaryActivity(characters, firstAt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get monthly activity for user {UserId}", userId);
            throw new RepositoryException("Failed to get monthly activity", ex);
        }
    }

    public async Task<SummaryClogPage> GetNewCollectionLogItems(int characterId, DateTimeOffset from, DateTimeOffset to, int limit)
    {
        try
        {
            // The collection-log predicate is the one GetActivityCalendar uses, two EXISTS and all,
            // because the overview's "N new slots" is that calendar's clog column summed: if this
            // list matched differently the count above it and the list under it would disagree.
            //
            // The kill ordinal is computed AFTER the limit, in the outer query, so the correlated
            // count runs once per shown entry rather than once per match. It is the same rule as
            // LootRecordRepository.GetKillOrdinals: the "<, or = and Id <=" tie-break, plus baseline.
            const string sql = """
                WITH firsts AS (
                    SELECT lr."Id" AS record_id, lr."SourceName", lr."OccurredAt", lr."KillCount",
                           ld."Id" AS drop_id, ld."ItemId", ld."Name", ld."Quantity", ld."Price",
                           COUNT(*) OVER ()::int AS total
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid
                      AND lr."OccurredAt" >= @from
                      AND lr."OccurredAt" < @to
                      AND ld."IsFirstTime" = true
                      AND (EXISTS (SELECT 1 FROM "EffectiveCollectionLogItems" cli WHERE cli."ItemId" = ld."ItemId")
                           OR EXISTS (SELECT 1 FROM "EffectiveCollectionLogItems" cli WHERE lower(cli."Name") = lower(ld."Name")))
                    ORDER BY lr."OccurredAt", lr."Id", ld."Id"
                    LIMIT @limit
                )
                SELECT f."OccurredAt", f."SourceName", f."ItemId", f."Name", f."Quantity",
                       (f."Quantity"::bigint * f."Price"::bigint) AS value,
                       f."KillCount",
                       ((SELECT COUNT(*) FROM "LootRecords" o
                          WHERE o."GameCharacterId" = @cid
                            AND o."SourceName" = f."SourceName"
                            AND (o."OccurredAt" < f."OccurredAt"
                                 OR (o."OccurredAt" = f."OccurredAt" AND o."Id" <= f.record_id)))
                        + COALESCE((SELECT b."BaselineKc" FROM "CharacterSourceBaselines" b
                                     WHERE b."GameCharacterId" = @cid AND b."SourceName" = f."SourceName"), 0))::int AS kill_ordinal,
                       f.total
                FROM firsts f
                ORDER BY f."OccurredAt", f.record_id, f.drop_id
                """;

            await using var cmd = await Command(sql);
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@from", from));
            cmd.Parameters.Add(new NpgsqlParameter("@to", to));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));

            var entries = new List<SummaryClogEntry>();
            var total = 0;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new SummaryClogEntry(
                    reader.GetFieldValue<DateTimeOffset>(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.GetInt32(7)));
                total = reader.GetInt32(8);
            }
            return new SummaryClogPage(entries, total);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get monthly collection-log items for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get monthly collection-log items", ex);
        }
    }

    public async Task<SummarySourcePage> GetSources(int characterId, DateTimeOffset from, DateTimeOffset to, int limit)
    {
        try
        {
            // The month's per-source aggregate is capped BEFORE the lifetime counts are taken, so the
            // correlated "kills before this month" runs only for the rows shown. Each is an
            // index-only count on IX_LootRecords_GameCharacterId_SourceName_OccurredAt_Id.
            const string sql = """
                WITH m AS (
                    SELECT "SourceName" AS source_name,
                           MIN("SourceType") AS source_type,
                           COUNT(*)::int AS kills,
                           COALESCE(SUM("TotalValue"), 0)::bigint AS gp,
                           MAX("KillCount") AS max_kc,
                           COUNT(*) OVER ()::int AS total_sources
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid
                      AND "OccurredAt" >= @from
                      AND "OccurredAt" < @to
                    GROUP BY "SourceName"
                    ORDER BY kills DESC, gp DESC, source_name
                    LIMIT @limit
                )
                SELECT m.source_name, m.source_type, m.kills, m.gp, m.max_kc, m.total_sources,
                       (SELECT COUNT(*) FROM "LootRecords" o
                         WHERE o."GameCharacterId" = @cid
                           AND o."SourceName" = m.source_name
                           AND o."OccurredAt" < @from)::int AS before,
                       COALESCE((SELECT b."BaselineKc" FROM "CharacterSourceBaselines" b
                                  WHERE b."GameCharacterId" = @cid AND b."SourceName" = m.source_name), 0) AS baseline
                FROM m
                ORDER BY m.kills DESC, m.gp DESC, m.source_name
                """;

            await using var cmd = await Command(sql);
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@from", from));
            cmd.Parameters.Add(new NpgsqlParameter("@to", to));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));

            var rows = new List<SummarySourceRow>();
            var total = 0;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new SummarySourceRow(
                    reader.GetString(0),
                    Enum.TryParse<LootSourceType>(reader.GetString(1), ignoreCase: true, out var st) ? st : LootSourceType.Unknown,
                    reader.GetInt32(2),
                    reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.GetInt32(6),
                    reader.GetInt32(7)));
                total = reader.GetInt32(5);
            }
            return new SummarySourcePage(rows, total);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get monthly sources for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get monthly sources", ex);
        }
    }

    public async Task<IReadOnlyList<SummaryDrop>> GetNotableDrops(int characterId, DateTimeOffset from, DateTimeOffset to, long floor, int limit)
    {
        try
        {
            // `lr."TotalValue" >= @floor` is implied by the drop-value predicate (a drop can never be
            // worth more than the kill it came from) and is there for the PLANNER: it prunes kills
            // before LootDrops is touched, so the join is driven from the month's valuable kills.
            // Without a floor at all, ranking one month's drops was a parallel seq scan of the whole
            // LootDrops table — 38ms on a 1M-row table and growing with it, against 12ms here.
            //
            // LootDrops is the effective projection, so item-value overrides are already applied and
            // blacklisted drops are already absent; nothing here needs to know about either.
            const string sql = """
                SELECT best."ItemId", best."Name", best."Quantity", best.value, best."SourceName", best."OccurredAt"
                FROM (
                    SELECT DISTINCT ON (ld."Name")
                           ld."ItemId", ld."Name", ld."Quantity",
                           (ld."Quantity"::bigint * ld."Price"::bigint) AS value,
                           lr."SourceName", lr."OccurredAt"
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid
                      AND lr."OccurredAt" >= @from
                      AND lr."OccurredAt" < @to
                      AND lr."TotalValue" >= @floor
                      AND ld."Quantity"::bigint * ld."Price"::bigint >= @floor
                    ORDER BY ld."Name", value DESC, lr."OccurredAt", ld."Id"
                ) best
                ORDER BY best.value DESC, best."OccurredAt", best."Name"
                LIMIT @limit
                """;

            await using var cmd = await Command(sql);
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@from", from));
            cmd.Parameters.Add(new NpgsqlParameter("@to", to));
            cmd.Parameters.Add(new NpgsqlParameter("@floor", floor));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));

            var drops = new List<SummaryDrop>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                drops.Add(new SummaryDrop(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5)));
            }
            return drops;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get monthly notable drops for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get monthly notable drops", ex);
        }
    }

    private async Task<DbCommand> Command(string sql)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }
}
