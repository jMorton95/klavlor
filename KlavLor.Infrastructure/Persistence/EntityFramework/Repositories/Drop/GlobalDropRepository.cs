using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Drop;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Drop;

// All-players view of a single dropped item. Mirrors GlobalSourceRepository, but pivots the
// aggregation from a source to an item: each query filters on ld."Name" = @item and groups by
// source / character / month.
internal sealed class GlobalDropRepository(DataContext dataContext, ILogger<GlobalDropRepository> logger)
    : IGlobalDropRepository
{
    // Only visible, non-admin-hidden, non-Leagues characters contribute — identical to the
    // global source page (a main-game view; seasonal Leagues loot lives in its own scope).
    private const string VisibilityFilter = """gc."IsVisible" = true AND gc."IsAdminHidden" = false AND gc."IsLeagues" = false""";

    // Optional single-character scope. Inlined rather than parameterised because it has to vanish
    // entirely (not become "AND x IS NULL") when absent; the value is an int the caller already
    // holds, so there is nothing here for a caller to inject.
    private static string CharacterFilter(int? gameCharacterId, string alias = "lr") =>
        gameCharacterId is { } id ? $"""AND {alias}."GameCharacterId" = {id}""" : "";

    public async Task<GlobalDropOverview?> GetOverview(string itemName)
    {
        try
        {
            var connection = await OpenConnection();

            var sql = $"""
                SELECT MAX(ld."ItemId")::int AS item_id,
                       COUNT(DISTINCT lr."Id")::bigint AS total_drops,
                       SUM(ld."Quantity"::bigint) AS total_qty,
                       SUM(ld."Quantity"::bigint * ld."Price"::bigint) AS total_value,
                       COUNT(DISTINCT lr."SourceName")::int AS distinct_sources,
                       COUNT(DISTINCT lr."GameCharacterId")::int AS distinct_characters,
                       COUNT(DISTINCT gc."UserId")::int AS distinct_players,
                       MIN(lr."OccurredAt") AS first_seen,
                       MAX(lr."OccurredAt") AS last_seen
                FROM "LootDrops" ld
                JOIN "LootRecords" lr ON lr."Id" = ld."LootRecordId"
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                WHERE ld."Name" = @item
                  AND {VisibilityFilter}
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@item", itemName));

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.IsDBNull(1) || reader.GetInt64(1) == 0)
                return null;

            return new GlobalDropOverview(
                itemName,
                reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get global drop overview for {Item}", itemName);
            throw new RepositoryException("Failed to get drop overview", ex);
        }
    }

    private static readonly Dictionary<string, string> SourceSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["source"] = "d.source_name",
        ["drops"] = "d.drops",
        ["kills"] = "kills",
        ["rate"] = "(d.drops::float / NULLIF(COALESCE(k.kills, 0), 0))",
        ["qty"] = "d.total_qty",
        ["value"] = "d.total_value",
        ["first"] = "d.first_seen",
        ["last"] = "d.last_seen",
    };

    public async Task<DropSourceTable> GetSources(string itemName, string sortBy, SortDirection direction, string? term)
    {
        try
        {
            var connection = await OpenConnection();
            var normalized = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
            var orderBy = ResolveOrderBy(SourceSortColumns, sortBy, direction, "d.total_value", "d.source_name");

            // drop_agg = per-source totals for this item; kill_agg = total kills at those
            // sources (the observed-rate denominator). Aggregating before the DropRates join
            // keeps the join from fanning out the sums (same shape as GlobalSourceRepository).
            var sql = $"""
                WITH drop_agg AS (
                    SELECT lr."SourceName" AS source_name,
                           mode() WITHIN GROUP (ORDER BY lr."SourceType") AS source_type,
                           COUNT(DISTINCT lr."Id")::bigint AS drops,
                           SUM(ld."Quantity"::bigint) AS total_qty,
                           SUM(ld."Quantity"::bigint * ld."Price"::bigint) AS total_value,
                           MIN(lr."OccurredAt") AS first_seen,
                           MAX(lr."OccurredAt") AS last_seen
                    FROM "LootDrops" ld
                    JOIN "LootRecords" lr ON lr."Id" = ld."LootRecordId"
                    JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                    WHERE ld."Name" = @item AND {VisibilityFilter}
                      AND (@term IS NULL OR lr."SourceName" ILIKE '%' || @term || '%')
                    GROUP BY lr."SourceName"
                ),
                kill_agg AS (
                    SELECT lr."SourceName" AS source_name, COUNT(*)::bigint AS kills
                    FROM "LootRecords" lr
                    JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                    WHERE lr."SourceName" IN (SELECT source_name FROM drop_agg) AND {VisibilityFilter}
                    GROUP BY lr."SourceName"
                )
                SELECT d.source_name, d.source_type::text, d.drops, COALESCE(k.kills, 0) AS kills,
                       d.total_qty, d.total_value,
                       dr."Rarity", dr."RarityNumerator", dr."RarityDenominator",
                       d.first_seen, d.last_seen, dr."Rolls"
                FROM drop_agg d
                LEFT JOIN kill_agg k ON k.source_name = d.source_name
                LEFT JOIN "DropRates" dr ON dr."SourceName" = d.source_name AND lower(dr."ItemName") = lower(@item)
                ORDER BY {orderBy}
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@item", itemName));
            cmd.Parameters.Add(new NpgsqlParameter("@term", NpgsqlTypes.NpgsqlDbType.Text)
            {
                Value = (object?)normalized ?? DBNull.Value
            });

            var rows = new List<DropSourceRow>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sourceType = !reader.IsDBNull(1)
                                 && Enum.TryParse<LootSourceType>(reader.GetString(1), ignoreCase: true, out var st)
                    ? st
                    : LootSourceType.Unknown;

                rows.Add(new DropSourceRow(
                    reader.GetString(0),
                    sourceType,
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                    reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    reader.GetFieldValue<DateTimeOffset>(9),
                    reader.GetFieldValue<DateTimeOffset>(10),
                    reader.IsDBNull(11) ? 1 : reader.GetInt32(11)));
            }

            return new DropSourceTable(
                rows,
                rows.Count,
                rows.Sum(r => r.Drops),
                rows.Sum(r => r.TotalQuantity),
                rows.Sum(r => r.TotalValue),
                normalized,
                sortBy,
                direction);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get sources for drop {Item}", itemName);
            throw new RepositoryException("Failed to get drop sources", ex);
        }
    }

    private static readonly Dictionary<string, string> CharacterSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["character"] = "character_name",
        ["drops"] = "drops",
        ["qty"] = "total_qty",
        ["value"] = "total_value",
        ["sources"] = "distinct_sources",
        ["first"] = "first_seen",
        ["last"] = "last_seen",
    };

    public async Task<DropCharacterSources?> GetCharacterSources(string itemName, int gameCharacterId)
    {
        try
        {
            var connection = await OpenConnection();

            // Same two-CTE shape as GetSources — per-source totals for the item, then total kills at
            // those sources — but narrowed to one character on both sides, so the kill count is that
            // character's own rolls rather than everyone's.
            var sql = $"""
                WITH drop_agg AS (
                    SELECT lr."SourceName" AS source_name,
                           mode() WITHIN GROUP (ORDER BY lr."SourceType") AS source_type,
                           COUNT(DISTINCT lr."Id")::bigint AS drops,
                           SUM(ld."Quantity"::bigint) AS total_qty,
                           SUM(ld."Quantity"::bigint * ld."Price"::bigint) AS total_value,
                           MIN(lr."OccurredAt") AS first_seen,
                           MAX(lr."OccurredAt") AS last_seen
                    FROM "LootDrops" ld
                    JOIN "LootRecords" lr ON lr."Id" = ld."LootRecordId"
                    JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                    WHERE ld."Name" = @item AND lr."GameCharacterId" = @cid AND {VisibilityFilter}
                    GROUP BY lr."SourceName"
                ),
                kill_agg AS (
                    SELECT lr."SourceName" AS source_name, COUNT(*)::bigint AS kills
                    FROM "LootRecords" lr
                    WHERE lr."GameCharacterId" = @cid
                      AND lr."SourceName" IN (SELECT source_name FROM drop_agg)
                    GROUP BY lr."SourceName"
                )
                SELECT d.source_name, d.source_type::text, d.drops, COALESCE(k.kills, 0) AS kills,
                       d.total_qty, d.total_value, d.first_seen, d.last_seen
                FROM drop_agg d
                LEFT JOIN kill_agg k ON k.source_name = d.source_name
                ORDER BY d.drops DESC, d.total_value DESC, d.source_name
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@item", itemName));
            cmd.Parameters.Add(new NpgsqlParameter("@cid", gameCharacterId));

            var rows = new List<DropCharacterSourceRow>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var sourceType = !reader.IsDBNull(1)
                                     && Enum.TryParse<LootSourceType>(reader.GetString(1), ignoreCase: true, out var st)
                        ? st
                        : LootSourceType.Unknown;

                    rows.Add(new DropCharacterSourceRow(
                        reader.GetString(0),
                        sourceType,
                        reader.GetInt64(2),
                        reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                        reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                        reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                        reader.GetFieldValue<DateTimeOffset>(6),
                        reader.GetFieldValue<DateTimeOffset>(7)));
                }
            }

            if (rows.Count == 0) return null;

            // Names resolved separately so the aggregate above stays a clean group-by.
            await using var nameCmd = connection.CreateCommand();
            nameCmd.CommandText = $"""
                SELECT COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       u."FirstName" || ' ' || u."LastName" AS user_name
                FROM "GameCharacters" gc
                JOIN "Users" u ON u."Id" = gc."UserId"
                WHERE gc."Id" = @cid AND {VisibilityFilter}
                """;
            nameCmd.Parameters.Add(new NpgsqlParameter("@cid", gameCharacterId));

            await using var nameReader = await nameCmd.ExecuteReaderAsync();
            if (!await nameReader.ReadAsync()) return null;
            var characterName = nameReader.GetString(0);
            var userName = nameReader.GetString(1);

            return new DropCharacterSources(
                gameCharacterId,
                characterName,
                userName,
                itemName,
                rows,
                rows.Sum(r => r.Drops),
                rows.Sum(r => r.TotalQuantity),
                rows.Sum(r => r.TotalValue),
                rows.Min(r => r.FirstSeen),
                rows.Max(r => r.LastSeen));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get sources of {Item} for character {CharacterId}", itemName, gameCharacterId);
            throw new RepositoryException("Failed to get character drop sources", ex);
        }
    }

    public async Task<DropCharacterTable> GetCharacters(string itemName, string sortBy, SortDirection direction, string? term)
    {
        try
        {
            var connection = await OpenConnection();
            var normalized = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
            var orderBy = ResolveOrderBy(CharacterSortColumns, sortBy, direction, "total_value", "character_name");

            var sql = $"""
                SELECT gc."Id",
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       u."FirstName" || ' ' || u."LastName" AS user_name,
                       COUNT(DISTINCT lr."Id")::bigint AS drops,
                       SUM(ld."Quantity"::bigint) AS total_qty,
                       SUM(ld."Quantity"::bigint * ld."Price"::bigint) AS total_value,
                       COUNT(DISTINCT lr."SourceName")::int AS distinct_sources,
                       MIN(lr."OccurredAt") AS first_seen,
                       MAX(lr."OccurredAt") AS last_seen
                FROM "LootDrops" ld
                JOIN "LootRecords" lr ON lr."Id" = ld."LootRecordId"
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u ON u."Id" = gc."UserId"
                WHERE ld."Name" = @item AND {VisibilityFilter}
                  AND (@term IS NULL OR COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") ILIKE '%' || @term || '%')
                GROUP BY gc."Id", gc."DisplayName", u."FirstName", u."LastName"
                ORDER BY {orderBy}
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@item", itemName));
            cmd.Parameters.Add(new NpgsqlParameter("@term", NpgsqlTypes.NpgsqlDbType.Text)
            {
                Value = (object?)normalized ?? DBNull.Value
            });

            var rows = new List<DropCharacterRow>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new DropCharacterRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                    reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                    reader.GetInt32(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    reader.GetFieldValue<DateTimeOffset>(8)));
            }

            return new DropCharacterTable(
                rows,
                rows.Count,
                rows.Sum(r => r.Drops),
                rows.Sum(r => r.TotalQuantity),
                rows.Sum(r => r.TotalValue),
                normalized,
                sortBy,
                direction);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get characters for drop {Item}", itemName);
            throw new RepositoryException("Failed to get drop characters", ex);
        }
    }

    // gameCharacterId scopes every query below to one character, which is what the per-character
    // drop page needs; null keeps the all-players view. One filter fragment, applied to both the
    // totals and the breakdown, so the stacked bars can never disagree with their own hover detail.
    public async Task<List<DropTrendPoint>> GetMonthlyTrend(string itemName, int? gameCharacterId = null)
    {
        try
        {
            var connection = await OpenConnection();

            // Monthly drop count + gp value of the item across all visible players, bucketed by
            // Europe/London date to match the per-character / source trends.
            var totalsSql = $"""
                SELECT EXTRACT(year FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS y,
                       EXTRACT(month FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS m,
                       COUNT(DISTINCT lr."Id")::bigint AS drops,
                       SUM(ld."Quantity"::bigint * ld."Price"::bigint)::bigint AS val
                FROM "LootDrops" ld
                JOIN "LootRecords" lr ON lr."Id" = ld."LootRecordId"
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                WHERE ld."Name" = @item AND {VisibilityFilter} {CharacterFilter(gameCharacterId)}
                GROUP BY 1, 2
                ORDER BY y, m
                """;

            var totals = new List<(int Y, int M, long Drops, long Value)>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = totalsSql;
                cmd.Parameters.Add(new NpgsqlParameter("@item", itemName));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    totals.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2),
                        reader.IsDBNull(3) ? 0 : reader.GetInt64(3)));
            }

            // Per (month, character, source) drop counts — drives the hover breakdown: each
            // character segment expands to the sources that contributed its drops that month.
            // Ordered by drops DESC so each character's sources come out highest-first (a
            // character's rows are a sorted subsequence of the month's rows).
            var breakdownSql = $"""
                SELECT EXTRACT(year FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS y,
                       EXTRACT(month FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS m,
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       lr."SourceName" AS source_name,
                       COUNT(DISTINCT lr."Id")::bigint AS drops
                FROM "LootDrops" ld
                JOIN "LootRecords" lr ON lr."Id" = ld."LootRecordId"
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u ON u."Id" = gc."UserId"
                WHERE ld."Name" = @item AND {VisibilityFilter} {CharacterFilter(gameCharacterId)}
                GROUP BY 1, 2, 3, 4
                ORDER BY y, m, drops DESC
                """;

            // (year, month) -> character (insertion order = drops desc) -> its source rows.
            var byMonth = new Dictionary<(int, int), Dictionary<string, List<DropTrendSource>>>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = breakdownSql;
                cmd.Parameters.Add(new NpgsqlParameter("@item", itemName));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var key = (reader.GetInt32(0), reader.GetInt32(1));
                    var character = reader.GetString(2);
                    var source = reader.GetString(3);
                    var drops = reader.GetInt64(4);

                    if (!byMonth.TryGetValue(key, out var byChar))
                    {
                        byChar = [];
                        byMonth[key] = byChar;
                    }
                    if (!byChar.TryGetValue(character, out var sources))
                    {
                        sources = [];
                        byChar[character] = sources;
                    }
                    sources.Add(new DropTrendSource(source, drops));
                }
            }

            return totals
                .Select(t =>
                {
                    var chars = byMonth.TryGetValue((t.Y, t.M), out var byChar)
                        ? byChar
                            .Select(kv => new DropTrendCharacter(kv.Key, kv.Value.Sum(s => s.Drops), kv.Value))
                            .OrderByDescending(c => c.Drops)
                            .ToList()
                        : [];
                    return new DropTrendPoint(t.Y, t.M, t.Drops, t.Value, chars);
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get monthly trend for drop {Item}", itemName);
            throw new RepositoryException("Failed to get drop monthly trend", ex);
        }
    }

    public async Task<List<DropSessionRow>> GetRecentSessions(string itemName, int limit, int? gameCharacterId = null)
    {
        try
        {
            var connection = await OpenConnection();

            // Group every visible character's kills into play sessions per (character, source) —
            // the same gap-based grouping as the character profile's session history — then keep
            // only the sessions that yielded this item, summarising just this item's contribution
            // (drops, quantity, value) alongside the full session size. Newest first.
            var sql = $"""
                WITH ordered AS (
                    SELECT lr."Id", lr."GameCharacterId", lr."SourceName", lr."SourceType",
                           lr."OccurredAt", lr."KillCount",
                           LAG(lr."OccurredAt") OVER (PARTITION BY lr."GameCharacterId", lr."SourceName" ORDER BY lr."OccurredAt", lr."Id") AS prev_at,
                           ROW_NUMBER() OVER (PARTITION BY lr."GameCharacterId", lr."SourceName" ORDER BY lr."OccurredAt", lr."Id") AS kill_ord
                    FROM "LootRecords" lr
                    JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                    WHERE {VisibilityFilter} {CharacterFilter(gameCharacterId, "lr")}
                ),
                {SessionSql.GapIslandsWithCap("\"GameCharacterId\", \"SourceName\"")},
                item_agg AS (
                    SELECT s."GameCharacterId", s."SourceName", s.session_no,
                           COUNT(DISTINCT s."Id")::int AS item_drops,
                           SUM(ld."Quantity"::bigint) AS item_qty,
                           SUM(ld."Quantity"::bigint * ld."Price"::bigint) AS item_value
                    FROM sessioned s
                    JOIN "LootDrops" ld ON ld."LootRecordId" = s."Id"
                    WHERE ld."Name" = @item
                    GROUP BY s."GameCharacterId", s."SourceName", s.session_no
                ),
                summ AS (
                    SELECT s."GameCharacterId", s."SourceName", MIN(s."SourceType") AS source_type, s.session_no,
                           MIN(s."OccurredAt") AS started, MAX(s."OccurredAt") AS ended,
                           COUNT(*)::int AS kills,
                           MIN(s."KillCount") AS min_kc, MAX(s."KillCount") AS max_kc,
                           MIN(s.kill_ord)::int AS min_ord, MAX(s.kill_ord)::int AS max_ord
                    FROM sessioned s
                    JOIN item_agg ia
                        ON ia."GameCharacterId" = s."GameCharacterId"
                       AND ia."SourceName" = s."SourceName"
                       AND ia.session_no = s.session_no
                    GROUP BY s."GameCharacterId", s."SourceName", s.session_no
                )
                SELECT su."GameCharacterId",
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       su."SourceName", su.source_type, su.session_no,
                       su.started, su.ended, su.kills, su.min_kc, su.max_kc, su.min_ord, su.max_ord,
                       ia.item_drops, ia.item_qty, ia.item_value
                FROM summ su
                JOIN item_agg ia
                    ON ia."GameCharacterId" = su."GameCharacterId"
                   AND ia."SourceName" = su."SourceName"
                   AND ia.session_no = su.session_no
                JOIN "GameCharacters" gc ON gc."Id" = su."GameCharacterId"
                JOIN "Users" u ON u."Id" = gc."UserId"
                ORDER BY su.ended DESC
                LIMIT @limit
                """;

            var sessions = new List<DropSessionRow>();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@item", itemName));
            cmd.Parameters.Add(new NpgsqlParameter("@gap", LootFeedGrouping.MaxGap));
            cmd.Parameters.Add(new NpgsqlParameter("@breakGap", LootFeedGrouping.SessionBreakGap));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sessions.Add(new DropSessionRow(
                    GameCharacterId: reader.GetInt32(0),
                    CharacterName: reader.GetString(1),
                    SourceName: reader.GetString(2),
                    SourceType: Enum.TryParse<LootSourceType>(reader.GetString(3), ignoreCase: true, out var st) ? st : LootSourceType.Unknown,
                    SessionIndex: (int)reader.GetInt64(4),
                    StartedAt: reader.GetFieldValue<DateTimeOffset>(5),
                    EndedAt: reader.GetFieldValue<DateTimeOffset>(6),
                    SessionKills: reader.GetInt32(7),
                    MinKillCount: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    MaxKillCount: reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    MinKillOrdinal: reader.GetInt32(10),
                    MaxKillOrdinal: reader.GetInt32(11),
                    ItemDrops: reader.GetInt32(12),
                    ItemQuantity: reader.IsDBNull(13) ? 0 : reader.GetInt64(13),
                    ItemValue: reader.IsDBNull(14) ? 0 : reader.GetInt64(14)));
            }

            return sessions;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get recent sessions for drop {Item}", itemName);
            throw new RepositoryException("Failed to get drop recent sessions", ex);
        }
    }

    private async Task<System.Data.Common.DbConnection> OpenConnection()
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        return connection;
    }

    // Maps a client-supplied sort key through a whitelist to a fixed SQL column expression,
    // so the raw value is never interpolated into the query. Falls back to the default column
    // when the key is unknown; appends the item-name column as a stable tiebreaker.
    private static string ResolveOrderBy(
        IReadOnlyDictionary<string, string> columns, string sortBy, SortDirection direction,
        string defaultColumn, string tiebreaker)
    {
        var column = columns.TryGetValue(sortBy ?? string.Empty, out var mapped) ? mapped : defaultColumn;
        var dir = direction == SortDirection.Ascending ? "ASC" : "DESC";
        return $"{column} {dir} NULLS LAST, {tiebreaker} ASC";
    }
}
