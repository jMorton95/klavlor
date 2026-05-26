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
                        THEN (d.elem - 'IsFirstTime') || '{"IsFirstTime": true}'::jsonb
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("duplicate key value violates unique constraint") == true
            || ex.InnerException?.Message.Contains("23505") == true;
    }
}
