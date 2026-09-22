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
                  -- A blacklisted drop cannot hold the first-time flag: it is invisible everywhere,
                  -- so the "first" badge would either sit on a drop nobody can see or be missing
                  -- from the earliest one anybody can. Excluded from the candidate set rather than
                  -- from the UPDATE, so the NEXT real receipt inherits the flag.
                  AND NOT EXISTS (
                      SELECT 1 FROM "BlacklistedLootDrops" b
                      WHERE b."LootRecordId" = lr."Id"
                        AND b."ItemId" = COALESCE((d.elem->>'ItemId')::int, 0)
                        AND lower(b."ItemName") = lower(COALESCE(d.elem->>'Name', ''))
                  )
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

    // Rebuilds the LootDrop projection for one character's records from their (canonical)
    // DropsJson. Used after a first-time reflag; also the recovery path if the projection ever
    // drifts. Delete + reinsert keeps it provably equal to DropsJson.
    //
    // "EQUAL TO DropsJson" DOES NOT MEAN "COPIED FROM IT". DropsJson holds the RAW RuneLite price by
    // design, while LootDrops.Price is the DERIVED projection and must hold the EFFECTIVE one - so
    // the admin's intrinsic value override has to be re-applied here, exactly as FinalizeDrops
    // applies it on ingest and RebuildForItem applies it on an override write. A straight copy
    // silently reset every overridden item to the raw figure it was overridden FOR (measured: a
    // record with TotalValue 5,000,000 left carrying LootDrops.Price 0), and it did so for the whole
    // character on any imported batch or special-drop injection - long after the admin had set the
    // value and seen it take effect.
    //
    // IsSpecial is carried across for the same reason: it lives in DropsJson, the feed's legendary
    // lane finds injected specials by querying LootDrops.IsSpecial, and dropping it here un-flagged
    // a special the moment SpecialLootHandler called RecomputeFirstTimeFlags right after writing it.
    //
    // TotalValue IS re-derived at the end, and must be. It used to be left alone on the reasoning
    // that this pass only restores the projection to what it should already have been and so cannot
    // change a price — true while the projection was one row per DropsJson entry, but the admin drop
    // blacklist removes entries, and a record whose drop has gone must lose that drop's gold with
    // it. The re-derivation is guarded by IS DISTINCT FROM, so in the ordinary case where nothing
    // moved it writes no rows at all. Keeping it here rather than at the blacklist call site is
    // deliberate: the projection and the total it rolls up are written by the same method, so they
    // cannot be left disagreeing by a caller that forgot the second half.
    public Task RebuildDropsForCharacter(int gameCharacterId) =>
        Rebuild("""lr."GameCharacterId" = @scope""", gameCharacterId);

    /// <summary>
    /// The same rebuild scoped to ONE record. What the drop blacklist uses: blacklisting a single
    /// item must not re-derive a whole character's projection, which for a heavily-farmed account is
    /// tens of thousands of rows deleted and reinserted to remove one.
    /// </summary>
    public Task RebuildDropsForRecord(int lootRecordId) =>
        Rebuild("""lr."Id" = @scope""", lootRecordId);

    // ONE spelling of the projection, parameterised by what it is scoped to. The two callers differ
    // only in that predicate; written twice they would drift, and a drift here means the character
    // page and the record it was rebuilt from disagree about what the kill contained.
    private async Task Rebuild(string scopePredicate, int scopeValue)
    {
        var deleteSql = $"""
            DELETE FROM "LootDrops" ld
            USING "LootRecords" lr
            WHERE ld."LootRecordId" = lr."Id" AND {scopePredicate}
            """;
        // The lateral gives the item id a name so the override join and the projected column can
        // both read it without spelling the COALESCE twice.
        var insertSql = $"""
            INSERT INTO "LootDrops" ("LootRecordId", "ItemId", "Name", "Quantity", "Price", "IsFirstTime", "IsSpecial")
            SELECT lr."Id",
                   d.item_id,
                   COALESCE(d.elem->>'Name', ''),
                   COALESCE((d.elem->>'Quantity')::int, 0),
                   COALESCE(ivo."Value", COALESCE((d.elem->>'Price')::int, 0)),
                   COALESCE((d.elem->>'IsFirstTime')::boolean, false),
                   COALESCE((d.elem->>'IsSpecial')::boolean, false)
            FROM "LootRecords" lr
            CROSS JOIN LATERAL (
                SELECT elem, COALESCE((elem->>'ItemId')::int, 0) AS item_id
                FROM jsonb_array_elements(lr."DropsJson") AS elem
            ) d
            LEFT JOIN "ItemValueOverrides" ivo ON ivo."ItemId" = d.item_id
            WHERE {scopePredicate}
              -- A blacklisted drop is absent from the projection, and that absence IS the feature:
              -- every SQL read site on the site queries LootDrops, so leaving the row out is what
              -- makes the item invisible on all of them without a single query changing. DropsJson
              -- above still holds it, which is what makes the decision reversible — lift the
              -- blacklist, run this again, and the row comes back exactly as it was.
              AND NOT EXISTS (
                  SELECT 1 FROM "BlacklistedLootDrops" b
                  WHERE b."LootRecordId" = lr."Id"
                    AND b."ItemId" = d.item_id
                    AND lower(b."ItemName") = lower(COALESCE(d.elem->>'Name', ''))
              )
            """;

        await dataContext.Database.ExecuteSqlRawAsync(deleteSql, new NpgsqlParameter("@scope", scopeValue));
        await dataContext.Database.ExecuteSqlRawAsync(insertSql, new NpgsqlParameter("@scope", scopeValue));

        // TotalValue is the rolled-up projection of the rows just written, so a blacklisted drop has
        // to leave the record's gold total as well as the drop grid. Left alone this would keep
        // quoting a total that no longer matches the sum of its own visible drops.
        await RecomputeTotals(scopePredicate, scopeValue);

        // EVERY LootDropRow THIS SCOPE IS TRACKING IS NOW A PHANTOM. The rebuild above is raw
        // set-based SQL: it DELETEs the projection rows and INSERTs new ones with new ids, and the
        // change tracker knows none of it. A tracked row still points at an id that no longer
        // exists, so the next tracked write touching it — most obviously a cascade from deleting the
        // record — issues a statement that matches nothing and throws DbUpdateConcurrencyException
        // ("expected 1, affected 0"). That is a false conflict: nobody edited anything, a derived
        // projection was rewritten underneath.
        //
        // Detaching is the right repair rather than reloading, and LootDropRow's own comment says
        // why: it has no independent lifecycle, audit trail or concurrency token — it is owned by
        // its LootRecord and fully rebuildable from DropsJson. Anything that needs these rows again
        // re-reads them, and gets the ones that actually exist.
        foreach (var stale in dataContext.ChangeTracker.Entries<LootDropRow>().ToList())
            stale.State = EntityState.Detached;
    }

    // Re-derives LootRecords.TotalValue from the LootDrops rows just written. Set-based and raw, so
    // the LootRecords rows pick up no audit or RowVersion churn — TotalValue is a derived
    // projection, not a user edit. Mirrors ItemValueOverrideRepository.RecomputeTotals, which does
    // the same job scoped to a batch of record ids.
    //
    // The LEFT JOIN LATERAL matters: a record whose every drop is blacklisted has no rows left at
    // all, and must fall to 0 rather than keep its old total by failing to match.
    private async Task RecomputeTotals(string scopePredicate, int scopeValue)
    {
        var sql = $"""
            UPDATE "LootRecords" target
            SET "TotalValue" = COALESCE(agg.total, 0)
            FROM "LootRecords" lr
            LEFT JOIN LATERAL (
                SELECT SUM(ld."Quantity"::bigint * ld."Price"::bigint) AS total
                FROM "LootDrops" ld
                WHERE ld."LootRecordId" = lr."Id"
            ) agg ON TRUE
            WHERE target."Id" = lr."Id"
              AND {scopePredicate}
              AND target."TotalValue" IS DISTINCT FROM COALESCE(agg.total, 0)
            """;

        await dataContext.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("@scope", scopeValue));
    }

    public async Task<int> GetKillOrdinal(int gameCharacterId, string sourceName, DateTimeOffset occurredAt, int recordId)
    {
        // Chronological position of (cid, source) for this record, tiebroken by Id
        // so two records with identical timestamps don't both claim the same ordinal.
        var ordinal = await dataContext.LootRecords.CountAsync(o =>
            o.GameCharacterId == gameCharacterId
            && o.SourceName == sourceName
            && (o.OccurredAt < occurredAt
                || (o.OccurredAt == occurredAt && o.Id <= recordId)));
        return ordinal + await GetBaseline(gameCharacterId, sourceName);
    }

    public async Task<Dictionary<int, int>> GetKillOrdinals(IReadOnlyCollection<KillOrdinalRequest> requests)
    {
        if (requests.Count == 0) return [];

        // One round-trip for the whole batch. The correlated count is the same indexed range scan
        // GetKillOrdinal does per record (IX_LootRecords_GameCharacterId_SourceName_OccurredAt_Id
        // covers it), and the same "< occurredAt, or = and Id <=" tie-break, so a record resolves to
        // the identical number either way - two spellings of this rule would drift and the feed card
        // and the roll ticker would disagree about the same kill.
        //
        // unnest WITH ORDINALITY rather than a temp table or one query per pair: the inputs arrive
        // as four parallel arrays and come back positionally, so nothing has to be joined on.
        var ids = requests.Select(r => r.RecordId).ToArray();
        var characterIds = requests.Select(r => r.GameCharacterId).ToArray();
        var sources = requests.Select(r => r.SourceName).ToArray();
        var occurredAt = requests.Select(r => r.OccurredAt).ToArray();

        const string sql = """
            SELECT r.rid,
                   (SELECT COUNT(*)
                      FROM "LootRecords" o
                     WHERE o."GameCharacterId" = r.cid
                       AND o."SourceName" = r.src
                       AND (o."OccurredAt" < r.at
                            OR (o."OccurredAt" = r.at AND o."Id" <= r.rid)))
                   + COALESCE((SELECT b."BaselineKc"
                                 FROM "CharacterSourceBaselines" b
                                WHERE b."GameCharacterId" = r.cid AND b."SourceName" = r.src), 0) AS ordinal
            FROM unnest(@rids, @cids, @srcs, @ats) AS r(rid, cid, src, at)
            """;

        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var cmd = (NpgsqlCommand)connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@rids", ids));
        cmd.Parameters.Add(new NpgsqlParameter("@cids", characterIds));
        cmd.Parameters.Add(new NpgsqlParameter("@srcs", sources));
        cmd.Parameters.Add(new NpgsqlParameter("@ats", occurredAt));

        var ordinals = new Dictionary<int, int>(requests.Count);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ordinals[reader.GetInt32(0)] = (int)reader.GetInt64(1);

        return ordinals;
    }

    // Admin baseline KC for a character at a source (kills before we had data), or 0.
    private async Task<int> GetBaseline(int gameCharacterId, string sourceName) =>
        await dataContext.CharacterSourceBaselines
            .Where(b => b.GameCharacterId == gameCharacterId && b.SourceName == sourceName)
            .Select(b => (int?)b.BaselineKc)
            .FirstOrDefaultAsync() ?? 0;

    public async Task<SessionKcBounds?> GetSessionBounds(
        int gameCharacterId, string sourceName, DateTimeOffset occurredAt, TimeSpan gap, TimeSpan breakGap)
    {
        try
        {
            var baseline = await GetBaseline(gameCharacterId, sourceName);
            // Sessions of this source's recent kills, split with the shared site rules and
            // anchored the way the global model would: the latest real break in the last 14
            // days, else the source's first kill ever (continuous grinders never break, so
            // their 16h chunks count from kill #1). Chunk numbers are arithmetic from that
            // anchor so only ±2 windows of kills are scanned. Describes the session containing
            // the newest kill.
            var sql = """
                WITH anchor AS (
                    SELECT COALESCE(
                        (SELECT max(b."OccurredAt") FROM (
                            SELECT r."OccurredAt",
                                   lag(r."OccurredAt") OVER (ORDER BY r."OccurredAt", r."Id") AS prev_at
                            FROM "LootRecords" r
                            WHERE r."GameCharacterId" = @cid AND r."SourceName" = @src
                              AND r."OccurredAt" >= @at - interval '14 days'
                              AND r."OccurredAt" <= @at
                        ) b
                        WHERE b.prev_at IS NOT NULL
                          AND ((b."OccurredAt" - b.prev_at) > @gap
                               OR ((b."OccurredAt" - b.prev_at) >= @breakGap
                                   AND date((b."OccurredAt" AT TIME ZONE 'Europe/London') - INTERVAL '6 hours')
                                    <> date((b.prev_at AT TIME ZONE 'Europe/London') - INTERVAL '6 hours')))),
                        (SELECT min(r."OccurredAt") FROM "LootRecords" r
                          WHERE r."GameCharacterId" = @cid AND r."SourceName" = @src)
                    ) AS s
                ),
                slice AS (
                    SELECT r."OccurredAt", r."Id", r."KillCount",
                           lag(r."OccurredAt") OVER (ORDER BY r."OccurredAt", r."Id") AS prev_at
                    FROM "LootRecords" r
                    WHERE r."GameCharacterId" = @cid AND r."SourceName" = @src
                      AND r."OccurredAt" >= greatest((SELECT a.s FROM anchor a), @at - @gap * 2)
                      AND r."OccurredAt" <= @at
                ),
                marked AS (
                    SELECT *, CASE WHEN prev_at IS NOT NULL
                                     AND (("OccurredAt" - prev_at) > @gap
                                          OR (("OccurredAt" - prev_at) >= @breakGap
                                              AND date(("OccurredAt" AT TIME ZONE 'Europe/London') - INTERVAL '6 hours')
                                               <> date((prev_at AT TIME ZONE 'Europe/London') - INTERVAL '6 hours')))
                                    THEN 1 ELSE 0 END AS brk
                    FROM slice
                ),
                based AS (
                    SELECT *, COALESCE(max(CASE WHEN brk = 1 THEN "OccurredAt" END)
                                         OVER (ORDER BY "OccurredAt", "Id" ROWS UNBOUNDED PRECEDING),
                                       (SELECT a.s FROM anchor a)) AS base
                    FROM marked
                ),
                chunked AS (
                    SELECT *, floor(extract(epoch FROM ("OccurredAt" - base))
                                    / extract(epoch FROM @gap))::int AS chunk
                    FROM based
                ),
                capped AS (
                    SELECT *, CASE WHEN brk = 1 OR chunk <> lag(chunk) OVER (ORDER BY "OccurredAt", "Id")
                                    THEN 1 ELSE 0 END AS new_sess
                    FROM chunked
                ),
                sessioned AS (
                    SELECT *, SUM(new_sess) OVER (ORDER BY "OccurredAt", "Id") AS session_no
                    FROM capped
                ),
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
                FirstOrdinal: reader.GetInt32(3) + baseline);
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
