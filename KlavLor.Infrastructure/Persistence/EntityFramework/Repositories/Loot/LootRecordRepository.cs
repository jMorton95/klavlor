using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class LootRecordRepository(DataContext dataContext, ILogger<LootRecordRepository> logger) : ILootRecordRepository
{
    public async Task<bool> SaveLootRecord(LootRecord record)
    {
        try
        {
            dataContext.LootRecords.Add(record);
            return await dataContext.SaveChangesAsync() > 0;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            logger.LogDebug("Duplicate loot record skipped for user {UserId}, hash {Hash}", record.UserId, record.ContentHash);
            dataContext.Entry(record).State = EntityState.Detached;
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save loot record for user {UserId}", record.UserId);
            throw new RepositoryException("Failed to save loot record", ex);
        }
    }

    public async Task<bool> SaveLootRecords(List<LootRecord> records)
    {
        try
        {
            dataContext.LootRecords.AddRange(records);
            return await dataContext.SaveChangesAsync() > 0;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Batch had duplicates — detach all and fall back to individual inserts
            logger.LogDebug("Batch insert hit duplicate constraint, falling back to individual inserts");
            foreach (var record in records)
                dataContext.Entry(record).State = EntityState.Detached;

            var inserted = 0;
            foreach (var record in records)
            {
                try
                {
                    dataContext.LootRecords.Add(record);
                    await dataContext.SaveChangesAsync();
                    inserted++;
                }
                catch (DbUpdateException inner) when (IsUniqueConstraintViolation(inner))
                {
                    dataContext.Entry(record).State = EntityState.Detached;
                }
            }

            logger.LogDebug("Individual fallback inserted {Inserted}/{Total} records", inserted, records.Count);
            return inserted > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save {Count} loot records", records.Count);
            throw new RepositoryException("Failed to save loot records", ex);
        }
    }

    public async Task<HashSet<string>> FindExistingHashes(int userId, IEnumerable<string> hashes)
    {
        var hashList = hashes.ToList();
        if (hashList.Count == 0)
            return [];

        var existing = await dataContext.LootRecords
            .Where(r => r.UserId == userId && r.ContentHash != null && hashList.Contains(r.ContentHash))
            .Select(r => r.ContentHash!)
            .ToListAsync();

        return existing.ToHashSet();
    }

    public async Task<HashSet<string>> GetSeenItemNames(int gameCharacterId, DateTimeOffset strictlyBefore)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        const string sql = """
            SELECT DISTINCT drop_elem->>'Name' AS item_name
            FROM "LootRecords" lr,
                 jsonb_array_elements(lr."DropsJson") AS drop_elem
            WHERE lr."GameCharacterId" = @cid
              AND lr."OccurredAt" < @t
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@cid", gameCharacterId));
        cmd.Parameters.Add(new NpgsqlParameter("@t", strictlyBefore));

        var seen = new HashSet<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0))
                seen.Add(reader.GetString(0));
        }
        return seen;
    }

    public async Task RecomputeFirstTimeFlags(int gameCharacterId)
    {
        // Rebuilds DropsJson for every record belonging to this character,
        // marking the earliest occurrence of each item as IsFirstTime=true
        // and clearing the flag everywhere else. Used after imported-history
        // batches that may slot in records earlier than already-saved ones.
        const string sql = """
            WITH unrolled AS (
                SELECT lr."Id" AS rec_id, lr."OccurredAt" AS t,
                       d.elem->>'Name' AS item_name, d.idx
                FROM "LootRecords" lr,
                     jsonb_array_elements(lr."DropsJson") WITH ORDINALITY AS d(elem, idx)
                WHERE lr."GameCharacterId" = @cid
            ),
            firsts AS (
                SELECT DISTINCT ON (item_name) rec_id, item_name
                FROM unrolled
                ORDER BY item_name, t, rec_id, idx
            )
            UPDATE "LootRecords" lr
            SET "DropsJson" = (
                SELECT jsonb_agg(
                    CASE
                        WHEN EXISTS (SELECT 1 FROM firsts f
                                     WHERE f.rec_id = lr."Id"
                                       AND f.item_name = d.elem->>'Name')
                        THEN (d.elem - 'IsFirstTime') || jsonb_build_object('IsFirstTime', true)
                        ELSE d.elem - 'IsFirstTime'
                    END
                    ORDER BY d.idx
                )
                FROM jsonb_array_elements(lr."DropsJson") WITH ORDINALITY AS d(elem, idx)
            )
            WHERE lr."GameCharacterId" = @cid
            """;

        await dataContext.Database.ExecuteSqlRawAsync(sql,
            new NpgsqlParameter("@cid", gameCharacterId));

        // DropsJson just changed (IsFirstTime flags moved), so the normalised LootDrop
        // projection for this character is now stale — rebuild it from the canonical JSON.
        await RebuildDropsForCharacter(gameCharacterId);
    }

    // Rebuilds the LootDrop projection for one character's records straight from their
    // (canonical) DropsJson. Used after a first-time reflag; also the recovery path if the
    // projection ever drifts. Delete + reinsert keeps it provably equal to DropsJson.
    public async Task RebuildDropsForCharacter(int gameCharacterId)
    {
        const string deleteSql = """
            DELETE FROM "LootDrops" ld
            USING "LootRecords" lr
            WHERE ld."LootRecordId" = lr."Id" AND lr."GameCharacterId" = @cid
            """;
        const string insertSql = """
            INSERT INTO "LootDrops" ("LootRecordId", "ItemId", "Name", "Quantity", "Price", "IsFirstTime")
            SELECT lr."Id",
                   COALESCE((d->>'ItemId')::int, 0),
                   COALESCE(d->>'Name', ''),
                   COALESCE((d->>'Quantity')::int, 0),
                   COALESCE((d->>'Price')::int, 0),
                   COALESCE((d->>'IsFirstTime')::boolean, false)
            FROM "LootRecords" lr,
                 jsonb_array_elements(lr."DropsJson") AS d
            WHERE lr."GameCharacterId" = @cid
            """;

        await dataContext.Database.ExecuteSqlRawAsync(deleteSql, new NpgsqlParameter("@cid", gameCharacterId));
        await dataContext.Database.ExecuteSqlRawAsync(insertSql, new NpgsqlParameter("@cid", gameCharacterId));
    }

    public async Task<int> GetKillOrdinal(int gameCharacterId, string sourceName, DateTimeOffset occurredAt, int recordId)
    {
        // Chronological position of (cid, source) for this record, tiebroken by Id
        // so two records with identical timestamps don't both claim the same ordinal.
        return await dataContext.LootRecords.CountAsync(o =>
            o.GameCharacterId == gameCharacterId
            && o.SourceName == sourceName
            && (o.OccurredAt < occurredAt
                || (o.OccurredAt == occurredAt && o.Id <= recordId)));
    }

    public async Task<SessionKcBounds?> GetSessionBounds(
        int gameCharacterId, string sourceName, DateTimeOffset occurredAt, TimeSpan gap, TimeSpan breakGap)
    {
        try
        {
            // Slice this source's kills around the given instant (a session can't start more than
            // one window earlier thanks to the hard cap — 2× for slack), split into sessions with
            // the shared rules, and describe the session containing the newest kill in the slice.
            var sql = $"""
                WITH ordered AS (
                    SELECT r."OccurredAt", r."Id", r."KillCount",
                           lag(r."OccurredAt") OVER (ORDER BY r."OccurredAt", r."Id") AS prev_at
                    FROM "LootRecords" r
                    WHERE r."GameCharacterId" = @cid AND r."SourceName" = @src
                      AND r."OccurredAt" >= @at - @gap * 2 AND r."OccurredAt" <= @at
                ),
                {SessionSql.GapIslandsWithCap("")},
                cur AS (
                    SELECT session_no FROM sessioned
                    ORDER BY "OccurredAt" DESC, "Id" DESC LIMIT 1
                ),
                bounds AS (
                    SELECT min(s."KillCount") AS min_kc, max(s."KillCount") AS max_kc,
                           min(s."OccurredAt") AS start_at
                    FROM sessioned s
                    WHERE s.session_no = (SELECT c.session_no FROM cur c)
                )
                SELECT b.min_kc, b.max_kc, b.start_at,
                       (SELECT count(*)::int FROM "LootRecords" o
                        WHERE o."GameCharacterId" = @cid AND o."SourceName" = @src
                          AND o."OccurredAt" < b.start_at) + 1 AS first_ordinal
                FROM bounds b
                WHERE b.start_at IS NOT NULL
                """;

            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", gameCharacterId));
            cmd.Parameters.Add(new NpgsqlParameter("@src", sourceName));
            cmd.Parameters.Add(new NpgsqlParameter("@at", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = occurredAt });
            cmd.Parameters.Add(new NpgsqlParameter("@gap", NpgsqlTypes.NpgsqlDbType.Interval) { Value = gap });
            cmd.Parameters.Add(new NpgsqlParameter("@breakGap", NpgsqlTypes.NpgsqlDbType.Interval) { Value = breakGap });

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new SessionKcBounds(
                MinKillCount: reader.IsDBNull(0) ? null : reader.GetInt32(0),
                MaxKillCount: reader.IsDBNull(1) ? null : reader.GetInt32(1),
                StartedAt: reader.GetFieldValue<DateTimeOffset>(2),
                FirstOrdinal: reader.GetInt32(3));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get session bounds for character {CharacterId} at {Source}", gameCharacterId, sourceName);
            throw new RepositoryException("Failed to get session bounds", ex);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("duplicate key value violates unique constraint") == true
            || ex.InnerException?.Message.Contains("23505") == true;
    }
}
