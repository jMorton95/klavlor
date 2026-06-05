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

internal sealed class LootLogRepository(DataContext dataContext, ILogger<LootLogRepository> logger, ICollectionLogCache collectionLogCache)
    : ILootLogRepository
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

            var visibilityFilter = includeHidden
                ? ""
                : """AND gc."IsVisible" = true AND gc."IsAdminHidden" = false""";

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
            var topDrops = await GetTopDropsForSource(characterId, group.SourceName);
            summaries.Add(new LootSourceSummary(
                group.SourceName,
                group.SourceType,
                group.TotalKills,
                group.TotalValue,
                topDrops));
        }

        return summaries;
    }

    private async Task<List<LootDropSummary>> GetTopDropsForSource(int characterId, string sourceName, int? limit = 5)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        // LEFT JOIN DropRates so the popover (and source-detail "all drops" panel) can
        // show "1/1024" next to the gp value. Aggregation happens in a CTE first so the
        // join doesn't fan out the SUMs.
        var sql = $"""
            WITH agg AS (
                SELECT ld."Name" as item_name,
                       SUM(ld."Quantity"::bigint) as total_qty,
                       SUM(ld."Quantity"::bigint * ld."Price"::bigint) as total_value
                FROM "LootRecords" lr
                JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                WHERE lr."GameCharacterId" = @characterId
                  AND lr."SourceName" = @sourceName
                GROUP BY ld."Name"
            )
            SELECT a.item_name, a.total_qty, a.total_value,
                   dr."Rarity", dr."RarityNumerator", dr."RarityDenominator"
            FROM agg a
            LEFT JOIN "DropRates" dr
                ON dr."SourceName" = @sourceName
               AND lower(dr."ItemName") = lower(a.item_name)
            ORDER BY a.total_value DESC
            {(limit.HasValue ? $"LIMIT {limit.Value}" : "")}
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@characterId", characterId));
        cmd.Parameters.Add(new NpgsqlParameter("@sourceName", sourceName));

        var drops = new List<LootDropSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            drops.Add(new LootDropSummary(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }

        return drops;
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

    public async Task<LootSourceDetail> GetSourceDetail(int characterId, string sourceName, int pageNumber, int pageSize)
    {
        try
        {
            // Whose log this is — drives the "X's loot from Y" page header. One cheap PK lookup.
            var characterName = await dataContext.GameCharacters
                .Where(c => c.Id == characterId)
                .Select(c => c.DisplayName ?? c.User!.FirstName + " " + c.User.LastName)
                .FirstOrDefaultAsync();

            var summary = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName)
                .GroupBy(r => new { r.SourceName, r.SourceType })
                .Select(g => new
                {
                    g.Key.SourceName,
                    g.Key.SourceType,
                    TotalKills = g.Count(),
                    TotalValue = g.Sum(r => r.TotalValue)
                })
                .FirstOrDefaultAsync();

            if (summary is null)
                return new LootSourceDetail(sourceName, LootSourceType.Unknown, 0, 0, [], [], false, CharacterName: characterName);

            var allDrops = await GetTopDropsForSource(characterId, sourceName, limit: null);

            // Notable drops: top 5 kills by value. Compute each record's chronological
            // ordinal via a correlated subquery so we can display a kill number even
            // when RuneLite didn't report one (KillCount = -1 → null).
            var notableRaw = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName)
                .OrderByDescending(r => r.TotalValue)
                .Take(5)
                .Select(r => new
                {
                    r.OccurredAt,
                    r.KillCount,
                    r.TotalValue,
                    r.DropsJson,
                    Ordinal = dataContext.LootRecords.Count(x =>
                        x.GameCharacterId == characterId
                        && x.SourceName == sourceName
                        && (x.OccurredAt < r.OccurredAt
                            || (x.OccurredAt == r.OccurredAt && x.Id <= r.Id)))
                })
                .ToListAsync();

            var notableDrops = notableRaw
                .Where(k => k.TotalValue > 0)
                .Select(k =>
                {
                    var drops = JsonSerializer.Deserialize<List<LootDrop>>(k.DropsJson) ?? [];
                    return new LootKillEntry(
                        k.OccurredAt,
                        k.KillCount,
                        k.Ordinal,
                        k.TotalValue,
                        drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime, collectionLogCache.IsCollectionLogItem(d.ItemId)))
                            .OrderByDescending(d => (long)d.Quantity * d.Price)
                            .ToList());
                }).ToList();

            var skip = (pageNumber - 1) * pageSize;
            var kills = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName)
                .OrderByDescending(r => r.OccurredAt)
                .Skip(skip)
                .Take(pageSize + 1)
                .Select(r => new { r.OccurredAt, r.KillCount, r.TotalValue, r.DropsJson })
                .ToListAsync();

            var hasMore = kills.Count > pageSize;
            var killEntries = kills.Take(pageSize).Select((k, i) =>
            {
                var drops = JsonSerializer.Deserialize<List<LootDrop>>(k.DropsJson) ?? [];
                var ordinal = summary.TotalKills - skip - i;
                return new LootKillEntry(
                    k.OccurredAt,
                    k.KillCount,
                    ordinal > 0 ? ordinal : null,
                    k.TotalValue,
                    drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime))
                        .OrderByDescending(d => (long)d.Quantity * d.Price)
                        .ToList());
            }).ToList();

            return new LootSourceDetail(
                summary.SourceName,
                summary.SourceType,
                summary.TotalKills,
                summary.TotalValue,
                allDrops,
                killEntries,
                hasMore,
                summary.TotalKills,
                notableDrops,
                characterName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source detail for character {CharacterId}, source {Source}", characterId, sourceName);
            throw new RepositoryException("Failed to get source detail", ex);
        }
    }

    public async Task<LootSourceDetail> GetSourceDetailKillsPage(int characterId, string sourceName, int pageNumber, int pageSize)
    {
        try
        {
            var totalKills = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName)
                .CountAsync();

            var skip = (pageNumber - 1) * pageSize;
            var kills = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName)
                .OrderByDescending(r => r.OccurredAt)
                .Skip(skip)
                .Take(pageSize + 1)
                .Select(r => new { r.OccurredAt, r.KillCount, r.TotalValue, r.DropsJson })
                .ToListAsync();

            var hasMore = kills.Count > pageSize;
            var killEntries = kills.Take(pageSize).Select((k, i) =>
            {
                var drops = JsonSerializer.Deserialize<List<LootDrop>>(k.DropsJson) ?? [];
                var ordinal = totalKills - skip - i;
                return new LootKillEntry(
                    k.OccurredAt,
                    k.KillCount,
                    ordinal > 0 ? ordinal : null,
                    k.TotalValue,
                    drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime))
                        .OrderByDescending(d => (long)d.Quantity * d.Price)
                        .ToList());
            }).ToList();

            return new LootSourceDetail(
                sourceName,
                LootSourceType.Unknown,
                totalKills,
                0,
                [],
                killEntries,
                hasMore,
                totalKills);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source detail kills page for character {CharacterId}, source {Source}", characterId, sourceName);
            throw new RepositoryException("Failed to get source detail kills page", ex);
        }
    }

    // Groups a character's kills at one source into play "sessions": consecutive kills with
    // no gap longer than LootFeedGrouping.MaxGap (mirrors the live feed). Gap-and-islands in
    // SQL (no JSONB) so it rides IX_LootRecords_GameCharacterId_SourceName; drops are then
    // aggregated only for the requested page of sessions.
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

            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var skip = (pageNumber - 1) * pageSize;

            // session_no starts at 1 for the oldest kill; rank orders newest-session-first for paging.
            const string sessionsSql = """
                WITH ordered AS (
                    SELECT "Id", "OccurredAt", "KillCount", "TotalValue",
                           LAG("OccurredAt") OVER (ORDER BY "OccurredAt", "Id") AS prev_at,
                           ROW_NUMBER() OVER (ORDER BY "OccurredAt", "Id") AS kill_ord
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid AND "SourceName" = @src
                ),
                marked AS (
                    SELECT *, CASE WHEN prev_at IS NULL OR ("OccurredAt" - prev_at) > @gap THEN 1 ELSE 0 END AS new_sess
                    FROM ordered
                ),
                sessioned AS (
                    SELECT *, SUM(new_sess) OVER (ORDER BY "OccurredAt", "Id") AS session_no
                    FROM marked
                ),
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
                SELECT session_no, started, ended, kills, min_kc, max_kc, min_ord, max_ord, total_gp, total_sessions
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
                cmd.Parameters.Add(new NpgsqlParameter("@skip", skip));
                cmd.Parameters.Add(new NpgsqlParameter("@take", pageSize));
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
                const string dropsSql = """
                    WITH ordered AS (
                        SELECT "Id", "OccurredAt", "DropsJson",
                               LAG("OccurredAt") OVER (ORDER BY "OccurredAt", "Id") AS prev_at
                        FROM "LootRecords"
                        WHERE "GameCharacterId" = @cid AND "SourceName" = @src
                    ),
                    marked AS (
                        SELECT *, CASE WHEN prev_at IS NULL OR ("OccurredAt" - prev_at) > @gap THEN 1 ELSE 0 END AS new_sess
                        FROM ordered
                    ),
                    sessioned AS (
                        SELECT *, SUM(new_sess) OVER (ORDER BY "OccurredAt", "Id") AS session_no
                        FROM marked
                    )
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
                        collectionLogCache.IsCollectionLogItem(reader.GetInt32(6))));
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
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            const string sql = """
                WITH ordered AS (
                    SELECT "Id", "OccurredAt", "KillCount", "TotalValue", "DropsJson",
                           LAG("OccurredAt") OVER (ORDER BY "OccurredAt", "Id") AS prev_at,
                           ROW_NUMBER() OVER (ORDER BY "OccurredAt", "Id") AS kill_ord
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid AND "SourceName" = @src
                ),
                marked AS (
                    SELECT *, CASE WHEN prev_at IS NULL OR ("OccurredAt" - prev_at) > @gap THEN 1 ELSE 0 END AS new_sess
                    FROM ordered
                ),
                sessioned AS (
                    SELECT *, SUM(new_sess) OVER (ORDER BY "OccurredAt", "Id") AS session_no
                    FROM marked
                )
                SELECT "OccurredAt", "KillCount", "TotalValue", "DropsJson", kill_ord
                FROM sessioned
                WHERE session_no = @sessionNo
                ORDER BY "OccurredAt" DESC, kill_ord DESC
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@src", sourceName));
            cmd.Parameters.Add(new NpgsqlParameter("@gap", LootFeedGrouping.MaxGap));
            cmd.Parameters.Add(new NpgsqlParameter("@sessionNo", (long)sessionNo));

            var entries = new List<LootKillEntry>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var json = reader.GetString(3);
                var drops = JsonSerializer.Deserialize<List<LootDrop>>(json) ?? [];
                entries.Add(new LootKillEntry(
                    reader.GetFieldValue<DateTimeOffset>(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    (int)reader.GetInt64(4),
                    reader.GetInt64(2),
                    drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime, collectionLogCache.IsCollectionLogItem(d.ItemId)))
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

            const string sessionsSql = """
                WITH ordered AS (
                    SELECT "Id", "SourceName", "SourceType", "OccurredAt", "KillCount", "TotalValue",
                           LAG("OccurredAt") OVER (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id") AS prev_at,
                           ROW_NUMBER() OVER (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id") AS kill_ord
                    FROM "LootRecords"
                    WHERE "GameCharacterId" = @cid
                ),
                marked AS (
                    SELECT *, CASE WHEN prev_at IS NULL OR ("OccurredAt" - prev_at) > @gap THEN 1 ELSE 0 END AS new_sess
                    FROM ordered
                ),
                sessioned AS (
                    SELECT *, SUM(new_sess) OVER (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id") AS session_no
                    FROM marked
                ),
                summ AS (
                    SELECT "SourceName", MIN("SourceType") AS source_type, session_no,
                           MIN("OccurredAt") AS started, MAX("OccurredAt") AS ended,
                           COUNT(*)::int AS kills,
                           MIN("KillCount") AS min_kc, MAX("KillCount") AS max_kc,
                           MIN(kill_ord)::int AS min_ord, MAX(kill_ord)::int AS max_ord,
                           SUM("TotalValue")::bigint AS total_gp
                    FROM sessioned GROUP BY "SourceName", session_no
                ),
                ranked AS (
                    SELECT *, ROW_NUMBER() OVER (ORDER BY ended DESC) AS rnk,
                           (COUNT(*) OVER ())::int AS total_sessions
                    FROM summ
                )
                SELECT "SourceName", source_type, session_no, started, ended, kills,
                       min_kc, max_kc, min_ord, max_ord, total_gp, total_sessions
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
                const string dropsSql = """
                    WITH ordered AS (
                        SELECT "Id", "SourceName", "OccurredAt",
                               LAG("OccurredAt") OVER (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id") AS prev_at
                        FROM "LootRecords"
                        WHERE "GameCharacterId" = @cid
                    ),
                    marked AS (
                        SELECT *, CASE WHEN prev_at IS NULL OR ("OccurredAt" - prev_at) > @gap THEN 1 ELSE 0 END AS new_sess
                        FROM ordered
                    ),
                    sessioned AS (
                        SELECT *, SUM(new_sess) OVER (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id") AS session_no
                        FROM marked
                    )
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
                        collectionLogCache.IsCollectionLogItem(reader.GetInt32(6))));
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
                sess AS (
                    SELECT "SourceName", SUM(new_sess)::int AS sessions
                    FROM (
                        SELECT "SourceName",
                               CASE WHEN LAG("OccurredAt") OVER w IS NULL
                                     OR ("OccurredAt" - LAG("OccurredAt") OVER w) > @gap
                                    THEN 1 ELSE 0 END AS new_sess
                        FROM base
                        WINDOW w AS (PARTITION BY "SourceName" ORDER BY "OccurredAt", "Id")
                    ) z GROUP BY "SourceName"
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

    public async Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> GetAllFeedTiers(
        int countPerTier, LootFeedScope scope = LootFeedScope.Main, IReadOnlySet<LootFeedTier>? requestedTiers = null)
    {
        var isLeagues = scope == LootFeedScope.Leagues;
        // The cap counts *grouped* entries: adjacent same-source kills collapse into one card
        // (e.g. 10 clues in a row = 1 entry), and we want countPerTier of those, not raw records.
        // Tier classification is per-drop (each LootRecord can split across tiers via its drops),
        // so we over-fetch candidates, filter+collapse in C#, and refetch with a larger window
        // only when one user's run dominated the initial fetch.
        const int initialTake = 150;
        const int hardCap = 1500;

        try
        {
            var tiers = requestedTiers ?? new HashSet<LootFeedTier>(ILootFeedService.AllTiers);
            var result = new Dictionary<LootFeedTier, List<LootFeedEntry>>();
            foreach (var tier in ILootFeedService.AllTiers)
                result[tier] = [];

            var baseQuery = dataContext.LootRecords
                .Where(r => r.GameCharacterId != null)
                .Join(dataContext.GameCharacters, r => r.GameCharacterId, gc => gc.Id, (r, gc) => new { Record = r, Character = gc })
                .Where(x => x.Character.IsVisible && !x.Character.IsAdminHidden && x.Character.IsLeagues == isLeagues)
                .Join(dataContext.Users, x => x.Character.UserId, u => u.Id, (x, u) => new { x.Record, x.Character, User = u });

            foreach (var tier in tiers)
            {
                var (tierMin, tierMax) = ILootFeedService.GetTierRange(tier);
                var take = initialTake;

                while (true)
                {
                    var candidates = await baseQuery
                        .Where(x => x.Record.TotalValue >= tierMin)
                        .OrderByDescending(x => x.Record.OccurredAt)
                        .Take(take)
                        .Select(x => new FeedTierProjection
                        {
                            UserName = x.User.FirstName + " " + x.User.LastName,
                            UserId = x.Record.UserId,
                            SourceName = x.Record.SourceName,
                            SourceType = x.Record.SourceType,
                            TotalValue = x.Record.TotalValue,
                            DropsJson = x.Record.DropsJson,
                            OccurredAt = x.Record.OccurredAt,
                            CharacterName = x.Character.DisplayName ?? x.User.FirstName + " " + x.User.LastName,
                            GameCharacterId = x.Character.Id,
                            KillCount = x.Record.KillCount,
                            // Per-character per-source chronological ordinal. Only used as a fallback
                            // label when RuneLite didn't supply a KillCount; see LootFeedItem.razor.
                            // The Id tiebreak avoids two equal-timestamp records sharing an ordinal.
                            KillOrdinal = dataContext.LootRecords.Count(o =>
                                o.GameCharacterId == x.Character.Id
                                && o.SourceName == x.Record.SourceName
                                && (o.OccurredAt < x.Record.OccurredAt
                                    || (o.OccurredAt == x.Record.OccurredAt && o.Id <= x.Record.Id)))
                        })
                        .ToListAsync();

                    var groups = CollapseProjections(candidates, tier, tierMin, tierMax, countPerTier, collectionLogCache, scope);

                    if (groups.Count >= countPerTier || candidates.Count < take || take >= hardCap)
                    {
                        result[tier] = groups;
                        break;
                    }

                    take = Math.Min(take * 2, hardCap);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get all feed tiers");
            throw new RepositoryException("Failed to get all feed tiers", ex);
        }
    }

    private static List<LootFeedEntry> CollapseProjections(
        List<FeedTierProjection> candidates,
        LootFeedTier tier,
        long tierMin,
        long? tierMax,
        int targetGroups,
        ICollectionLogCache collectionLogCache,
        LootFeedScope scope)
    {
        var groups = new List<LootFeedEntry>();
        // GroupKey -> indices into `groups`. Lets us match records to any same-key group within
        // 1h, not just the previous one — needed for interleaved sources (e.g. Shades of Mort'ton
        // gold keys of different colours).
        var indexByKey = new Dictionary<string, List<int>>();

        foreach (var r in candidates)
        {
            var allDrops = JsonSerializer.Deserialize<List<LootDrop>>(r.DropsJson) ?? [];
            var tierDrops = allDrops
                .Where(d =>
                {
                    var val = (long)d.Quantity * d.Price;
                    return val >= tierMin && (tierMax is null || val < tierMax.Value);
                })
                .Select(d => new LootFeedDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime, collectionLogCache.IsCollectionLogItem(d.ItemId)))
                .ToList();

            if (tierDrops.Count == 0) continue;

            var tierTotal = tierDrops.Sum(d => (long)d.Quantity * d.Price);
            var entry = new LootFeedEntry(
                r.UserName,
                r.UserId,
                r.SourceName,
                r.SourceType,
                tierTotal,
                tierDrops,
                r.OccurredAt,
                tier,
                r.CharacterName,
                r.GameCharacterId,
                MinKillCount: r.KillCount,
                MaxKillCount: r.KillCount,
                MinKillOrdinal: r.KillOrdinal,
                MaxKillOrdinal: r.KillOrdinal,
                Scope: scope);

            var bestIndex = -1;
            var bestDelta = TimeSpan.MaxValue;
            if (indexByKey.TryGetValue(entry.GroupKey, out var candidateIndices))
            {
                foreach (var i in candidateIndices)
                {
                    var delta = LootFeedGrouping.TryGetMergeDelta(groups[i], entry);
                    if (delta is null) continue;
                    if (delta.Value < bestDelta)
                    {
                        bestDelta = delta.Value;
                        bestIndex = i;
                    }
                }
            }

            if (bestIndex >= 0)
            {
                groups[bestIndex] = LootFeedGrouping.Merge(groups[bestIndex], entry);
            }
            else
            {
                groups.Add(entry);
                if (!indexByKey.TryGetValue(entry.GroupKey, out var list))
                {
                    list = [];
                    indexByKey[entry.GroupKey] = list;
                }
                list.Add(groups.Count - 1);
            }

            if (groups.Count >= targetGroups) break;
        }
        return groups;
    }

    public async Task<CharacterDayFeed> GetCharacterDayFeed(int characterId, DateOnly day)
    {
        try
        {
            // Day boundaries in Europe/London (matching GetActivityCalendar's bucketing),
            // converted to UTC for the OccurredAt range filter.
            var start = IngestTimezone.FromLocalNaive(day.ToDateTime(TimeOnly.MinValue));
            var end = IngestTimezone.FromLocalNaive(day.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var baseQuery = dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId
                            && r.OccurredAt >= start
                            && r.OccurredAt < end);

            // True totals for the day — every kill, not just the valued ones shown as cards.
            var summary = await baseQuery
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Kills = g.Count(),
                    Gp = g.Sum(r => r.TotalValue),
                    Sources = g.Select(r => r.SourceName).Distinct().Count()
                })
                .FirstOrDefaultAsync();

            // Same join + projection shape as GetAllFeedTiers (LootRecord has no nav properties).
            var candidates = await baseQuery
                .Join(dataContext.GameCharacters, r => r.GameCharacterId, gc => gc.Id, (r, gc) => new { Record = r, Character = gc })
                .Join(dataContext.Users, x => x.Character.UserId, u => u.Id, (x, u) => new { x.Record, x.Character, User = u })
                .OrderByDescending(x => x.Record.OccurredAt)
                .Select(x => new FeedTierProjection
                {
                    UserName = x.User.FirstName + " " + x.User.LastName,
                    UserId = x.Record.UserId,
                    SourceName = x.Record.SourceName,
                    SourceType = x.Record.SourceType,
                    TotalValue = x.Record.TotalValue,
                    DropsJson = x.Record.DropsJson,
                    OccurredAt = x.Record.OccurredAt,
                    CharacterName = x.Character.DisplayName ?? x.User.FirstName + " " + x.User.LastName,
                    GameCharacterId = x.Character.Id,
                    KillCount = x.Record.KillCount,
                    KillOrdinal = dataContext.LootRecords.Count(o =>
                        o.GameCharacterId == x.Character.Id
                        && o.SourceName == x.Record.SourceName
                        && (o.OccurredAt < x.Record.OccurredAt
                            || (o.OccurredAt == x.Record.OccurredAt && o.Id <= x.Record.Id)))
                })
                .ToListAsync();

            var entries = CollapseDay(candidates, collectionLogCache);

            return new CharacterDayFeed(
                day,
                summary?.Kills ?? 0,
                summary?.Gp ?? 0,
                summary?.Sources ?? 0,
                entries);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get day feed for character {CharacterId} on {Day}", characterId, day);
            throw new RepositoryException("Failed to get day feed", ex);
        }
    }

    // Like CollapseProjections, but keeps every valued drop (>=10K) across all tiers rather than
    // splitting per tier, and has no group cap — a whole day's valued kills, merged into runs.
    private static List<LootFeedEntry> CollapseDay(
        List<FeedTierProjection> candidates,
        ICollectionLogCache collectionLogCache)
    {
        var groups = new List<LootFeedEntry>();
        var indexByKey = new Dictionary<string, List<int>>();

        foreach (var r in candidates)
        {
            var allDrops = JsonSerializer.Deserialize<List<LootDrop>>(r.DropsJson) ?? [];
            var drops = allDrops
                .Where(d => ILootFeedService.GetDropTier((long)d.Quantity * d.Price) is not null)
                .Select(d => new LootFeedDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime, collectionLogCache.IsCollectionLogItem(d.ItemId)))
                .ToList();

            if (drops.Count == 0) continue;

            var total = drops.Sum(d => (long)d.Quantity * d.Price);
            var entry = new LootFeedEntry(
                r.UserName,
                r.UserId,
                r.SourceName,
                r.SourceType,
                total,
                drops,
                r.OccurredAt,
                ILootFeedService.GetDropTier(total) ?? LootFeedTier.Standard,
                r.CharacterName,
                r.GameCharacterId,
                MinKillCount: r.KillCount,
                MaxKillCount: r.KillCount,
                MinKillOrdinal: r.KillOrdinal,
                MaxKillOrdinal: r.KillOrdinal);

            var bestIndex = -1;
            var bestDelta = TimeSpan.MaxValue;
            if (indexByKey.TryGetValue(entry.GroupKey, out var candidateIndices))
            {
                foreach (var i in candidateIndices)
                {
                    var delta = LootFeedGrouping.TryGetMergeDelta(groups[i], entry);
                    if (delta is null) continue;
                    if (delta.Value < bestDelta)
                    {
                        bestDelta = delta.Value;
                        bestIndex = i;
                    }
                }
            }

            if (bestIndex >= 0)
            {
                groups[bestIndex] = LootFeedGrouping.Merge(groups[bestIndex], entry);
            }
            else
            {
                groups.Add(entry);
                if (!indexByKey.TryGetValue(entry.GroupKey, out var list))
                {
                    list = [];
                    indexByKey[entry.GroupKey] = list;
                }
                list.Add(groups.Count - 1);
            }
        }

        // Re-classify each (possibly merged) run by its final combined value so column bucketing
        // reflects the card's real total, not just its first kill.
        return groups
            .Select(g => g with { Tier = ILootFeedService.GetDropTier(g.TotalValue) ?? LootFeedTier.Standard })
            .ToList();
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

    private static NpgsqlParameter NullableTimestampParam(string name, DateTimeOffset? value) =>
        new(name, NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = (object?)value ?? DBNull.Value
        };

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
                      AND EXISTS (
                          SELECT 1 FROM "EffectiveCollectionLogItems" cli
                          WHERE cli."ItemId" = ld."ItemId"
                      )
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
                      AND EXISTS (
                          SELECT 1 FROM "EffectiveCollectionLogItems" cli
                          WHERE cli."ItemId" = ld."ItemId"
                      )
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

            // Top 35 (item, source) contributors per month, by drop value. The first
            // ~10 stack as named segments in the chart; the rest feed the "Other"
            // segment's expanded tooltip. Separate query keeps the monthly-aggregate
            // plan simple and avoids re-unrolling DropsJson inside its CTE.
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
                WHERE rn <= 35
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
                var drops = JsonSerializer.Deserialize<List<LootDrop>>(topKillRaw.DropsJson) ?? [];
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

    public async Task<SourceCollection> GetSourceCollection(int characterId, string sourceName)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // KillCount/KillOrdinal correspond to the *earliest* LootRecord containing
            // each item — i.e. the KC at which the item first dropped for this character.
            // DISTINCT ON picks that earliest row per item; the correlated subquery
            // computes the chronological ordinal (fallback when RuneLite gave no KC).
            // Restrict to items that are in the real OSRS collection log so the tab
            // matches its name; the in-game "All Drops" panel on LootLogSourceDetail
            // still shows everything regardless of clog status.
            const string sql = """
                WITH unrolled AS (
                    SELECT lr."Id", lr."OccurredAt", lr."KillCount",
                           ld."Name" AS item_name,
                           ld."Quantity"::bigint AS qty,
                           (ld."Quantity"::bigint * ld."Price"::bigint) AS value,
                           ld."IsFirstTime" AS first_time
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid AND lr."SourceName" = @source
                      AND EXISTS (
                          SELECT 1 FROM "EffectiveCollectionLogItems" cli
                          WHERE cli."ItemId" = ld."ItemId"
                      )
                ),
                agg AS (
                    SELECT item_name,
                           MIN("OccurredAt") AS first_received,
                           MAX("OccurredAt") AS last_received,
                           COUNT(*)::int AS total_drops,
                           SUM(qty) AS total_qty,
                           SUM(value) AS total_value,
                           bool_or(first_time) AS has_first
                    FROM unrolled
                    GROUP BY item_name
                ),
                first_row AS (
                    SELECT DISTINCT ON (item_name)
                           item_name, "Id", "OccurredAt", "KillCount"
                    FROM unrolled
                    ORDER BY item_name, "OccurredAt" ASC, "Id" ASC
                ),
                last_row AS (
                    SELECT DISTINCT ON (item_name)
                           item_name, "Id", "OccurredAt", "KillCount"
                    FROM unrolled
                    ORDER BY item_name, "OccurredAt" DESC, "Id" DESC
                )
                SELECT a.item_name, a.first_received, a.last_received, a.total_drops,
                       a.total_qty, a.total_value, a.has_first,
                       f."KillCount" AS first_kc,
                       (SELECT COUNT(*)::int FROM "LootRecords" o
                        WHERE o."GameCharacterId" = @cid
                          AND o."SourceName" = @source
                          AND (o."OccurredAt" < f."OccurredAt"
                               OR (o."OccurredAt" = f."OccurredAt" AND o."Id" <= f."Id"))) AS first_ordinal,
                       l."KillCount" AS last_kc,
                       (SELECT COUNT(*)::int FROM "LootRecords" o
                        WHERE o."GameCharacterId" = @cid
                          AND o."SourceName" = @source
                          AND (o."OccurredAt" < l."OccurredAt"
                               OR (o."OccurredAt" = l."OccurredAt" AND o."Id" <= l."Id"))) AS last_ordinal,
                       dr."Rarity", dr."RarityNumerator", dr."RarityDenominator", dr."Rolls"
                FROM agg a
                JOIN first_row f ON f.item_name = a.item_name
                JOIN last_row l ON l.item_name = a.item_name
                LEFT JOIN "DropRates" dr
                    ON dr."SourceName" = @source
                   AND lower(dr."ItemName") = lower(a.item_name)
                ORDER BY a.first_received ASC
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));

            var entries = new List<CollectionEntry>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    entries.Add(new CollectionEntry(
                        ItemName: reader.GetString(0),
                        FirstReceivedAt: reader.GetFieldValue<DateTimeOffset>(1),
                        LastReceivedAt: reader.GetFieldValue<DateTimeOffset>(2),
                        TotalDrops: reader.GetInt32(3),
                        TotalQuantity: reader.GetInt64(4),
                        TotalValue: reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                        MarkedFirstTime: !reader.IsDBNull(6) && reader.GetBoolean(6),
                        KillCount: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                        KillOrdinal: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        LastKillCount: reader.IsDBNull(9) ? null : reader.GetInt32(9),
                        LastKillOrdinal: reader.IsDBNull(10) ? null : reader.GetInt32(10),
                        Rarity: reader.IsDBNull(11) ? null : reader.GetString(11),
                        RarityNumerator: reader.IsDBNull(12) ? null : reader.GetInt32(12),
                        RarityDenominator: reader.IsDBNull(13) ? null : reader.GetInt32(13),
                        Rolls: reader.IsDBNull(14) ? 1 : reader.GetInt32(14)));
                }
            }

            // Missing items: every clog entry whose Tabs array contains the source name,
            // minus those the character has already received from this source. Empty when
            // the source has no clog tab mapping (e.g. unrecognised RuneLite source name).
            // LEFT JOIN DropRates so rarest items naturally float to the top of the panel.
            var missingItems = new List<MissingClogItem>();
            await using (var missingCmd = connection.CreateCommand())
            {
                missingCmd.CommandText = """
                    SELECT cli."Name", dr."Rarity", dr."RarityNumerator", dr."RarityDenominator", dr."Rolls"
                    FROM "EffectiveCollectionLogItems" cli
                    LEFT JOIN "DropRates" dr
                        ON dr."SourceName" = @source
                       AND lower(dr."ItemName") = lower(cli."Name")
                    WHERE @source = ANY (cli."Tabs")
                      AND NOT EXISTS (
                          SELECT 1 FROM "LootRecords" lr
                          JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                          WHERE lr."GameCharacterId" = @cid
                            AND lr."SourceName" = @source
                            AND ld."ItemId" = cli."ItemId"
                      )
                    ORDER BY dr."RarityDenominator" DESC NULLS LAST, cli."Name"
                    """;
                missingCmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                missingCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                await using var missingReader = await missingCmd.ExecuteReaderAsync();
                while (await missingReader.ReadAsync())
                {
                    missingItems.Add(new MissingClogItem(
                        ItemName: missingReader.GetString(0),
                        Rarity: missingReader.IsDBNull(1) ? null : missingReader.GetString(1),
                        RarityNumerator: missingReader.IsDBNull(2) ? null : missingReader.GetInt32(2),
                        RarityDenominator: missingReader.IsDBNull(3) ? null : missingReader.GetInt32(3),
                        Rolls: missingReader.IsDBNull(4) ? 1 : missingReader.GetInt32(4)));
                }
            }

            // Character KC at this source — denominator for luck/expected calcs. Prefer
            // RuneLite's reported KillCount (matches the in-game counter the player sees);
            // fall back to the logged-row count when KillCount was never reported.
            // Using COUNT instead understates progress when the player started syncing
            // partway through their kills, which made luck pills nonsensical for any
            // multi-drop item (e.g. 8 drops shown as "Spooned 30×" because the implicit
            // KC was a fraction of the real in-game value).
            var maxKc = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName && r.KillCount != null && r.KillCount > 0)
                .MaxAsync(r => (int?)r.KillCount);
            int characterKc;
            if (maxKc.HasValue)
            {
                characterKc = maxKc.Value;
            }
            else
            {
                characterKc = await dataContext.LootRecords
                    .CountAsync(r => r.GameCharacterId == characterId && r.SourceName == sourceName);
            }

            // Per-item drop events. Drives the KC-column hover popover that lists every
            // drop occurrence for an item. Only rows that show up as clog entries get
            // populated, so the join cost is bounded.
            var eventsByItem = new Dictionary<string, List<DropEvent>>(StringComparer.OrdinalIgnoreCase);
            if (entries.Count > 0)
            {
                await using var eventsCmd = connection.CreateCommand();
                eventsCmd.CommandText = """
                    SELECT ld."Name" AS item_name,
                           lr."OccurredAt",
                           lr."KillCount",
                           (SELECT COUNT(*)::int FROM "LootRecords" o
                            WHERE o."GameCharacterId" = @cid
                              AND o."SourceName" = @source
                              AND (o."OccurredAt" < lr."OccurredAt"
                                   OR (o."OccurredAt" = lr."OccurredAt" AND o."Id" <= lr."Id"))) AS kill_ordinal
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid AND lr."SourceName" = @source
                      AND EXISTS (
                          SELECT 1 FROM "EffectiveCollectionLogItems" cli
                          WHERE cli."ItemId" = ld."ItemId"
                      )
                    ORDER BY ld."Name", lr."OccurredAt" ASC, lr."Id" ASC
                    """;
                eventsCmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                eventsCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                await using var eventsReader = await eventsCmd.ExecuteReaderAsync();
                while (await eventsReader.ReadAsync())
                {
                    var name = eventsReader.GetString(0);
                    if (!eventsByItem.TryGetValue(name, out var list))
                    {
                        list = new List<DropEvent>();
                        eventsByItem[name] = list;
                    }
                    list.Add(new DropEvent(
                        eventsReader.GetFieldValue<DateTimeOffset>(1),
                        eventsReader.IsDBNull(2) ? null : eventsReader.GetInt32(2),
                        eventsReader.IsDBNull(3) ? null : eventsReader.GetInt32(3)));
                }

                for (var i = 0; i < entries.Count; i++)
                {
                    if (eventsByItem.TryGetValue(entries[i].ItemName, out var ev))
                        entries[i] = entries[i] with { DropEvents = ev };
                }
            }

            return new SourceCollection(sourceName, characterKc, entries, missingItems);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source collection for character {CharacterId}, source {Source}", characterId, sourceName);
            throw new RepositoryException("Failed to get source collection", ex);
        }
    }

    public async Task<SourcePopoverData> GetSourcePopover(int characterId, string sourceName)
    {
        try
        {
            // Summary row: KC + total GP for this character at this source. Empty rows
            // get a zeroed payload so the caller can still render the boss icon/name.
            var summary = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalKills = g.Count(),
                    TotalValue = g.Sum(r => r.TotalValue)
                })
                .FirstOrDefaultAsync();

            var topDrops = await GetTopDropsForSource(characterId, sourceName, limit: 5);

            // Collection-log progress: numerator = distinct clog items this character has
            // ever received from this source; denominator = clog items whose Tabs array
            // contains the source name. Tabs is wiki-synced and can be unmapped for some
            // sources (e.g. minigames named differently in RuneLite) — when ClogTotal=0
            // the UI degrades to "X clog items" without the fraction.
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            int clogUnlocked;
            await using (var unlockedCmd = connection.CreateCommand())
            {
                unlockedCmd.CommandText = """
                    SELECT COUNT(DISTINCT ld."Name")::int
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid
                      AND lr."SourceName" = @source
                      AND EXISTS (
                          SELECT 1 FROM "EffectiveCollectionLogItems" cli
                          WHERE cli."ItemId" = ld."ItemId"
                            AND @source = ANY (cli."Tabs")
                      )
                    """;
                unlockedCmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                unlockedCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                var raw = await unlockedCmd.ExecuteScalarAsync();
                clogUnlocked = raw is null or DBNull ? 0 : Convert.ToInt32(raw);
            }

            int clogTotal;
            await using (var totalCmd = connection.CreateCommand())
            {
                totalCmd.CommandText = """
                    SELECT COUNT(*)::int FROM "EffectiveCollectionLogItems"
                    WHERE @source = ANY ("Tabs")
                    """;
                totalCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                var raw = await totalCmd.ExecuteScalarAsync();
                clogTotal = raw is null or DBNull ? 0 : Convert.ToInt32(raw);
            }

            return new SourcePopoverData(
                sourceName,
                summary?.TotalKills ?? 0,
                summary?.TotalValue ?? 0,
                clogUnlocked,
                clogTotal,
                topDrops);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source popover for character {CharacterId}, source {Source}", characterId, sourceName);
            throw new RepositoryException("Failed to get source popover", ex);
        }
    }

    public async Task<FirstTimeFeed> GetFirstTimeFeed(int characterId, DateTimeOffset? before, int pageSize)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // Pull one extra so we know whether more exist beyond this page.
            var take = pageSize + 1;
            // KillOrdinal = chronological position of this kill within (character, source).
            // Same correlated-count pattern as GetSourceDetail, used as fallback when
            // RuneLite didn't report an in-game KillCount.
            const string sql = """
                SELECT lr."OccurredAt",
                       lr."SourceName",
                       lr."SourceType"::text,
                       ld."Name" AS item_name,
                       ld."Quantity" AS qty,
                       (ld."Quantity"::bigint * ld."Price"::bigint) AS value,
                       lr."KillCount",
                       (SELECT COUNT(*)::int FROM "LootRecords" o
                        WHERE o."GameCharacterId" = lr."GameCharacterId"
                          AND o."SourceName" = lr."SourceName"
                          AND (o."OccurredAt" < lr."OccurredAt"
                               OR (o."OccurredAt" = lr."OccurredAt" AND o."Id" <= lr."Id"))) AS kill_ordinal
                FROM "LootRecords" lr
                JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                WHERE lr."GameCharacterId" = @cid
                  AND ld."IsFirstTime" = true
                  AND EXISTS (
                      SELECT 1 FROM "EffectiveCollectionLogItems" cli
                      WHERE cli."ItemId" = ld."ItemId"
                  )
                  AND (@before IS NULL OR lr."OccurredAt" < @before)
                ORDER BY lr."OccurredAt" DESC
                LIMIT @take
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(NullableTimestampParam("@before", before));
            cmd.Parameters.Add(new NpgsqlParameter("@take", take));

            var rows = new List<FirstTimeEntry>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sourceType = Enum.TryParse<LootSourceType>(reader.GetString(2), ignoreCase: true, out var st)
                    ? st : LootSourceType.Unknown;
                rows.Add(new FirstTimeEntry(
                    reader.GetFieldValue<DateTimeOffset>(0),
                    reader.GetString(1),
                    sourceType,
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7)));
            }

            var hasMore = rows.Count > pageSize;
            var page = hasMore ? rows.Take(pageSize).ToList() : rows;
            DateTimeOffset? nextBefore = hasMore && page.Count > 0 ? page[^1].OccurredAt : null;
            return new FirstTimeFeed(page, nextBefore, hasMore);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get first-time feed for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get first-time feed", ex);
        }
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
                top_source AS (
                    SELECT item_name, "SourceName", SUM(value) AS src_value,
                           ROW_NUMBER() OVER (PARTITION BY item_name ORDER BY SUM(value) DESC) AS rn
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

    private sealed class FeedTierProjection
    {
        public required string UserName { get; init; }
        public required int UserId { get; init; }
        public required string SourceName { get; init; }
        public required LootSourceType SourceType { get; init; }
        public required long TotalValue { get; init; }
        public required string DropsJson { get; init; }
        public required DateTimeOffset OccurredAt { get; init; }
        public required string CharacterName { get; init; }
        public required int GameCharacterId { get; init; }
        public int? KillCount { get; init; }
        public int? KillOrdinal { get; init; }
    }
}
