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

// The admin sync log, the public character list, and the per-character log search + sources table
// - i.e. everything LootLogHandler's search surfaces and IngestLogHandler need. Split out of
// LootLogRepository by consumer feature; the queries are unchanged.
internal sealed class LootLogSearchRepository(
    DataContext dataContext, ILogger<LootLogSearchRepository> logger)
    : ILootLogSearchRepository
{
    // Admin "Sync Log": every ingested record, newest-ingest-first, across all users/characters.
    // Ordered by SavedAt (ingest time) so it reads as a live feed of what clients have sent.
    public async Task<IngestLogResult> GetIngestLog(IngestLogQuery query)
    {
        try
        {
            var filtered = query.IncludeBackfill
                ? dataContext.LootRecords
                : dataContext.LootRecords.Where(r => !r.IsImported);

            var total = await filtered.CountAsync();
            var liveCount = await dataContext.LootRecords.CountAsync(r => !r.IsImported);
            var backfillCount = await dataContext.LootRecords.CountAsync(r => r.IsImported);

            var skip = (query.PageNumber - 1) * query.PageSize;

            // Left joins via nullable navigations — legacy records without a linked character
            // should still appear in the audit log.
            var rows = await filtered
                .OrderByDescending(r => r.SavedAt)
                .ThenByDescending(r => r.Id)
                .Skip(skip)
                .Take(query.PageSize + 1)
                .Select(r => new
                {
                    r.Id,
                    r.SavedAt,
                    r.OccurredAt,
                    r.SourceName,
                    r.SourceType,
                    r.KillCount,
                    r.IsImported,
                    r.DropsJson,
                    r.GameCharacterId,
                    CharacterDisplayName = r.GameCharacter != null ? r.GameCharacter.DisplayName : null,
                    UserFirstName = r.User!.FirstName,
                    UserLastName = r.User.LastName
                })
                .ToListAsync();

            var hasMore = rows.Count > query.PageSize;

            var entries = rows.Take(query.PageSize).Select(r =>
            {
                var drops = JsonSerializer.Deserialize<List<LootDrop>>(r.DropsJson) ?? [];
                var userName = $"{r.UserFirstName} {r.UserLastName}".Trim();
                var characterName = r.CharacterDisplayName ?? (r.GameCharacterId != null ? userName : null);
                var itemNames = drops
                    .Select(d => d.Quantity > 1 ? $"{d.Name} ×{d.Quantity:N0}" : d.Name)
                    .ToList();

                return new IngestLogEntry(
                    r.Id, r.SavedAt, r.OccurredAt, userName, characterName,
                    r.SourceName, r.SourceType, r.KillCount, r.IsImported, itemNames);
            }).ToList();

            return new IngestLogResult(entries, hasMore, total, liveCount, backfillCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get ingest log");
            throw new RepositoryException("Failed to get ingest log", ex);
        }
    }

    public async Task<List<LootLogCharacterSummary>> GetCharactersWithLoot(bool includeHidden = false)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            // Public list = main-game scope: visible, not admin-hidden, not Leagues
            // (seasonal Leagues characters live in their own feed scope). includeHidden
            // is the admin "show everything" path and applies no filter.
            var visibilityFilter = includeHidden
                ? ""
                : """AND gc."IsVisible" = true AND gc."IsAdminHidden" = false AND gc."IsLeagues" = false""";

            var sql = $"""
                SELECT gc."Id",
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") as "CharacterName",
                       u."FirstName" || ' ' || u."LastName" as "UserName",
                       COUNT(DISTINCT lr."SourceName")::int as "TotalSources",
                       COUNT(*)::bigint as "TotalKills",
                       SUM(lr."TotalValue") as "TotalValue"
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u ON u."Id" = gc."UserId"
                WHERE lr."GameCharacterId" IS NOT NULL
                {visibilityFilter}
                GROUP BY gc."Id", gc."DisplayName", gc."RuneLiteId", u."FirstName", u."LastName"
                ORDER BY "TotalValue" DESC
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            var characters = new List<LootLogCharacterSummary>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                characters.Add(new LootLogCharacterSummary(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5)));
            }

            return characters;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get characters with loot");
            throw new RepositoryException("Failed to get characters with loot", ex);
        }
    }

    public async Task<LootLogSearchResult> SearchLootLog(int characterId, LootLogQuery query)
    {
        try
        {
            var baseQuery = dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId);

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm;
                baseQuery = baseQuery.Where(r => EF.Functions.ILike(r.SourceName, $"%{term}%"));
            }

            var totalCount = await baseQuery
                .GroupBy(r => new { r.SourceName, r.SourceType })
                .CountAsync();

            var sourceMatchesRaw = await GetSourceMatches(characterId, query, fetchExtra: true);
            var hasMore = sourceMatchesRaw.Count > query.PageSize;
            var sourceMatches = sourceMatchesRaw.Take(query.PageSize).ToList();

            var itemMatches = new List<LootItemAggregate>();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var matchedSourceNames = sourceMatches.Select(s => s.SourceName).ToHashSet();
                itemMatches = await GetItemMatches(characterId, query.SearchTerm, matchedSourceNames);
            }

            return new LootLogSearchResult(sourceMatches, itemMatches, hasMore, query.SearchTerm, totalCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search loot log for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to search loot log", ex);
        }
    }

    private async Task<List<LootSourceSummary>> GetSourceMatches(int characterId, LootLogQuery query, bool fetchExtra = false)
    {
        var baseQuery = dataContext.LootRecords
            .Where(r => r.GameCharacterId == characterId);

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm;
            baseQuery = baseQuery.Where(r => EF.Functions.ILike(r.SourceName, $"%{term}%"));
        }

        var take = fetchExtra ? query.PageSize + 1 : query.PageSize;

        var grouped = await baseQuery
            .GroupBy(r => new { r.SourceName, r.SourceType })
            .Select(g => new
            {
                g.Key.SourceName,
                g.Key.SourceType,
                TotalKills = g.Count(),
                TotalValue = g.Sum(r => r.TotalValue)
            })
            .OrderByDescending(g => g.TotalValue)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(take)
            .ToListAsync();

        var summaries = new List<LootSourceSummary>();

        foreach (var group in grouped)
        {
            var topDrops = await GetTopDropsForSource(dataContext, characterId, group.SourceName);
            summaries.Add(new LootSourceSummary(
                group.SourceName,
                group.SourceType,
                group.TotalKills,
                group.TotalValue,
                topDrops));
        }

        return summaries;
    }

    private async Task<List<LootItemAggregate>> GetItemMatches(
        int characterId, string searchTerm, HashSet<string> excludeSourceNames)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        // Fetch all (source, item) rows matching the term, then group by item in C#
        // so the same item across many sources collapses into a single aggregate row.
        const string sql = """
            SELECT lr."SourceName", lr."SourceType"::text,
                   COUNT(DISTINCT lr."Id") as "TotalKills",
                   ld."Name" as "ItemName",
                   SUM(ld."Quantity"::bigint) as "TotalQuantity",
                   SUM(ld."Quantity"::bigint * ld."Price"::bigint) as "TotalItemValue"
            FROM "LootRecords" lr
            JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
            WHERE lr."GameCharacterId" = @characterId
              AND ld."Name" ILIKE '%' || @searchTerm || '%'
              AND lr."SourceName" NOT ILIKE '%' || @searchTerm || '%'
            GROUP BY lr."SourceName", lr."SourceType", ld."Name"
            ORDER BY "TotalItemValue" DESC
            LIMIT 500
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@characterId", characterId));
        cmd.Parameters.Add(new NpgsqlParameter("@searchTerm", searchTerm));

        var rows = new List<LootItemSourceRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var sourceName = reader.GetString(0);
            if (excludeSourceNames.Contains(sourceName))
                continue;

            rows.Add(new LootItemSourceRow(
                sourceName,
                reader.GetString(1),
                (int)reader.GetInt64(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5)));
        }

        return rows
            .GroupBy(r => r.ItemName)
            .Select(g => new LootItemAggregate(
                g.Key,
                g.Sum(r => r.TotalQuantity),
                g.Sum(r => r.TotalItemValue),
                g.Count(),
                g.OrderByDescending(r => r.TotalItemValue)
                    .Select(r => new LootItemSourceBreakdown(
                        r.SourceName, r.SourceType, r.TotalKills, r.TotalQuantity, r.TotalItemValue))
                    .ToList()))
            .OrderByDescending(a => a.TotalValue)
            .Take(25)
            .ToList();
    }

    private sealed record LootItemSourceRow(
        string SourceName,
        string SourceType,
        int TotalKills,
        string ItemName,
        long TotalQuantity,
        long TotalItemValue);

    // Whitelist of sortable columns → safe ORDER BY expressions (never interpolate the raw
    // SortBy string). Keyed by the token the table headers emit.
    private static readonly Dictionary<string, string> SourceTableSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "src.\"SourceName\"",
        ["type"] = "src.source_type",
        ["kills"] = "src.kills",
        ["value"] = "src.total_value",
        ["first"] = "src.first_seen",
        ["last"] = "src.last_seen",
        ["sessions"] = "COALESCE(sess.sessions, 0)",
        ["items"] = "COALESCE(dr.distinct_items, 0)",
        ["drops"] = "COALESCE(dr.total_drops, 0)",
        ["biggest"] = "COALESCE(big.v, 0)",
        ["clog"] = "COALESCE(cl.unlocked, 0)"
    };

    // Per-character sources table: one indexed pass per page producing every surfaced metric
    // (kills, gp, first/last seen, sessions, distinct items, total drops, biggest drop, clog
    // unlocked/total), server-side sorted by a whitelisted column. Totals are computed across
    // every matching source, not just the page.
    public async Task<SourceTable> GetCharacterSourceTable(int characterId, LootLogQuery query)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var term = string.IsNullOrWhiteSpace(query.SearchTerm) ? null : query.SearchTerm.Trim();
            var sortKey = query.SortBy is not null && SourceTableSortColumns.ContainsKey(query.SortBy)
                ? query.SortBy.ToLowerInvariant()
                : "value";
            var sortExpr = SourceTableSortColumns[sortKey];
            var dir = query.SortDirection == SortDirection.Ascending ? "ASC" : "DESC";
            var skip = (query.PageNumber - 1) * query.PageSize;

            var rows = new List<SourceTableRow>();
            var totalSources = 0;

            var sql = $"""
                WITH base AS (
                    SELECT lr."Id", lr."SourceName", lr."SourceType", lr."OccurredAt", lr."TotalValue"
                    FROM "LootRecords" lr
                    WHERE lr."GameCharacterId" = @cid
                      AND (@term IS NULL OR lr."SourceName" ILIKE '%' || @term || '%')
                ),
                src AS (
                    SELECT "SourceName", MAX("SourceType") AS source_type,
                           COUNT(*)::bigint AS kills, SUM("TotalValue")::bigint AS total_value,
                           MIN("OccurredAt") AS first_seen, MAX("OccurredAt") AS last_seen
                    FROM base GROUP BY "SourceName"
                ),
                ordered AS (
                    SELECT "Id", "SourceName", "OccurredAt",
                           LAG("OccurredAt") OVER (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id") AS prev_at
                    FROM base
                ),
                {SessionSql.GapIslandsWithCap("\"SourceName\"")},
                sess AS (
                    SELECT "SourceName", COUNT(DISTINCT session_no)::int AS sessions
                    FROM sessioned GROUP BY "SourceName"
                ),
                dr AS (
                    SELECT b."SourceName",
                           COUNT(DISTINCT ld."Name")::int AS distinct_items,
                           COUNT(*)::bigint AS total_drops
                    FROM base b JOIN "LootDrops" ld ON ld."LootRecordId" = b."Id"
                    GROUP BY b."SourceName"
                ),
                big AS (
                    SELECT DISTINCT ON (b."SourceName") b."SourceName",
                           ld."Name" AS biggest_name,
                           (ld."Quantity"::bigint * ld."Price"::bigint) AS v
                    FROM base b JOIN "LootDrops" ld ON ld."LootRecordId" = b."Id"
                    ORDER BY b."SourceName", v DESC
                ),
                cl AS (
                    SELECT b."SourceName", COUNT(DISTINCT ld."ItemId")::int AS unlocked
                    FROM base b JOIN "LootDrops" ld ON ld."LootRecordId" = b."Id"
                    WHERE ld."IsFirstTime"
                      AND EXISTS (SELECT 1 FROM "EffectiveCollectionLogItems" cli WHERE cli."ItemId" = ld."ItemId")
                    GROUP BY b."SourceName"
                ),
                clt AS (
                    SELECT s."SourceName", COUNT(*)::int AS total
                    FROM (SELECT DISTINCT "SourceName" FROM base) s
                    JOIN "EffectiveCollectionLogItems" cli ON s."SourceName" = ANY (cli."Tabs")
                    GROUP BY s."SourceName"
                )
                SELECT src."SourceName", src.source_type, src.kills, src.total_value,
                       src.first_seen, src.last_seen,
                       COALESCE(sess.sessions, 0) AS sessions,
                       COALESCE(dr.distinct_items, 0) AS distinct_items,
                       COALESCE(dr.total_drops, 0) AS total_drops,
                       big.biggest_name, COALESCE(big.v, 0) AS biggest_value,
                       COALESCE(cl.unlocked, 0) AS clog_unlocked,
                       COALESCE(clt.total, 0) AS clog_total,
                       (COUNT(*) OVER ())::int AS total_sources
                FROM src
                LEFT JOIN sess ON sess."SourceName" = src."SourceName"
                LEFT JOIN dr ON dr."SourceName" = src."SourceName"
                LEFT JOIN big ON big."SourceName" = src."SourceName"
                LEFT JOIN cl ON cl."SourceName" = src."SourceName"
                LEFT JOIN clt ON clt."SourceName" = src."SourceName"
                ORDER BY {sortExpr} {dir} NULLS LAST, src."SourceName" ASC
                OFFSET @skip LIMIT @take
                """;

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                cmd.Parameters.Add(new NpgsqlParameter("@term", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)term ?? DBNull.Value });
                cmd.Parameters.Add(new NpgsqlParameter("@gap", LootFeedGrouping.MaxGap));
                cmd.Parameters.Add(new NpgsqlParameter("@breakGap", LootFeedGrouping.SessionBreakGap));
                cmd.Parameters.Add(new NpgsqlParameter("@skip", skip));
                cmd.Parameters.Add(new NpgsqlParameter("@take", query.PageSize));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add(new SourceTableRow(
                        reader.GetString(0),
                        Enum.TryParse<LootSourceType>(reader.GetString(1), ignoreCase: true, out var st) ? st : LootSourceType.Unknown,
                        reader.GetInt64(2),
                        reader.GetInt64(3),
                        reader.GetFieldValue<DateTimeOffset>(4),
                        reader.GetFieldValue<DateTimeOffset>(5),
                        reader.GetInt32(6),
                        reader.GetInt32(7),
                        reader.GetInt64(8),
                        reader.IsDBNull(9) ? null : reader.GetString(9),
                        reader.GetInt64(10),
                        reader.GetInt32(11),
                        reader.GetInt32(12)));
                    totalSources = reader.GetInt32(13);
                }
            }

            // Totals across the full matching set (not just this page).
            var totals = new SourceTableTotals(0, 0, 0, 0, 0);
            const string totalsSql = """
                WITH base AS (
                    SELECT lr."Id", lr."SourceName", lr."TotalValue"
                    FROM "LootRecords" lr
                    WHERE lr."GameCharacterId" = @cid
                      AND (@term IS NULL OR lr."SourceName" ILIKE '%' || @term || '%')
                )
                SELECT (SELECT COUNT(DISTINCT "SourceName") FROM base)::int,
                       (SELECT COUNT(*) FROM base)::bigint,
                       (SELECT COALESCE(SUM("TotalValue"), 0) FROM base)::bigint,
                       (SELECT COUNT(DISTINCT ld."Name") FROM base b JOIN "LootDrops" ld ON ld."LootRecordId" = b."Id")::bigint,
                       (SELECT COUNT(*) FROM base b JOIN "LootDrops" ld ON ld."LootRecordId" = b."Id")::bigint
                """;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = totalsSql;
                cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                cmd.Parameters.Add(new NpgsqlParameter("@term", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)term ?? DBNull.Value });
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                    totals = new SourceTableTotals(
                        reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2),
                        reader.GetInt64(3), reader.GetInt64(4));
            }

            return new SourceTable(
                rows, totals,
                skip + rows.Count < totalSources,
                totalSources,
                term, sortKey, query.SortDirection);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source table for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get source table", ex);
        }
    }
}
