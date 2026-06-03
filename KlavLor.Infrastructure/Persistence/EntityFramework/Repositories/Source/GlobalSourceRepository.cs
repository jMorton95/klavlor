using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Source;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Source;

internal sealed class GlobalSourceRepository(DataContext dataContext, ILogger<GlobalSourceRepository> logger)
    : IGlobalSourceRepository
{
    // Only visible, non-admin-hidden, non-Leagues characters contribute. The global
    // source page is a main-game view; seasonal Leagues loot lives in its own feed scope
    // and must not bleed into these aggregates.
    private const string VisibilityFilter = """gc."IsVisible" = true AND gc."IsAdminHidden" = false AND gc."IsLeagues" = false""";

    public async Task<GlobalSourceOverview?> GetOverview(string sourceName)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            // mode() picks the most common SourceType for the name (it's near-always
            // consistent, but RuneLite occasionally varies the classification).
            var sql = $"""
                SELECT mode() WITHIN GROUP (ORDER BY lr."SourceType") AS source_type,
                       COUNT(*)::bigint AS total_kills,
                       SUM(lr."TotalValue")::bigint AS total_value,
                       COUNT(DISTINCT lr."GameCharacterId")::int AS distinct_characters,
                       COUNT(DISTINCT gc."UserId")::int AS distinct_players,
                       MIN(lr."OccurredAt") AS first_seen,
                       MAX(lr."OccurredAt") AS last_seen
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                WHERE lr."SourceName" = @source
                  AND {VisibilityFilter}
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync() || reader.IsDBNull(1) || reader.GetInt64(1) == 0)
                return null;

            var sourceType = !reader.IsDBNull(0)
                             && Enum.TryParse<LootSourceType>(reader.GetString(0), ignoreCase: true, out var st)
                ? st
                : LootSourceType.Unknown;

            return new GlobalSourceOverview(
                sourceName,
                sourceType,
                reader.GetInt64(1),
                reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get global source overview for {Source}", sourceName);
            throw new RepositoryException("Failed to get source overview", ex);
        }
    }

    public Task<List<GlobalSourceDrop>> GetTopDrops(string sourceName, int limit)
        => QueryDrops(sourceName, skip: 0, take: limit);

    // Top item drops by value across all visible characters. CTE aggregates before the
    // DropRates join so the join can't fan out the sums (same shape as
    // LootLogRepository.GetTopDropsForSource).
    private async Task<List<GlobalSourceDrop>> QueryDrops(string sourceName, int skip, int take)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var sql = $"""
                WITH agg AS (
                    SELECT drop_elem->>'Name' AS item_name,
                           SUM((drop_elem->>'Quantity')::bigint) AS total_qty,
                           SUM((drop_elem->>'Quantity')::bigint * (drop_elem->>'Price')::bigint) AS total_value
                    FROM "LootRecords" lr
                    JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                       , jsonb_array_elements(lr."DropsJson") AS drop_elem
                    WHERE lr."SourceName" = @source
                      AND {VisibilityFilter}
                    GROUP BY drop_elem->>'Name'
                )
                SELECT a.item_name, a.total_qty, a.total_value,
                       dr."Rarity", dr."RarityNumerator", dr."RarityDenominator"
                FROM agg a
                LEFT JOIN "DropRates" dr
                    ON dr."SourceName" = @source
                   AND lower(dr."ItemName") = lower(a.item_name)
                ORDER BY a.total_value DESC NULLS LAST
                OFFSET @skip LIMIT @take
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
            cmd.Parameters.Add(new NpgsqlParameter("@skip", skip));
            cmd.Parameters.Add(new NpgsqlParameter("@take", take));

            var drops = new List<GlobalSourceDrop>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                drops.Add(new GlobalSourceDrop(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5)));
            }

            return drops;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to query drops for source {Source}", sourceName);
            throw new RepositoryException("Failed to query source drops", ex);
        }
    }

    public async Task<List<SourcePlayerRow>> GetPlayers(string sourceName, int limit)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var sql = $"""
                SELECT gc."Id",
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       u."FirstName" || ' ' || u."LastName" AS user_name,
                       COUNT(*)::bigint AS total_kills,
                       SUM(lr."TotalValue")::bigint AS total_value
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u ON u."Id" = gc."UserId"
                WHERE lr."SourceName" = @source
                  AND {VisibilityFilter}
                GROUP BY gc."Id", gc."DisplayName", u."FirstName", u."LastName"
                ORDER BY total_kills DESC, total_value DESC
                LIMIT @limit
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));

            var players = new List<SourcePlayerRow>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                players.Add(new SourcePlayerRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt64(4)));
            }

            return players;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get players for source {Source}", sourceName);
            throw new RepositoryException("Failed to get source players", ex);
        }
    }

    public async Task<List<SourceClogEvent>> GetRecentClogs(string sourceName, int limit)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            // First-time collection-log unlocks that happened at this source: a drop
            // flagged IsFirstTime (first time that character received it) whose item is a
            // real collection-log entry. Newest first — a running "who logged what" feed.
            var sql = $"""
                SELECT lr."OccurredAt",
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       gc."Id" AS game_character_id,
                       drop_elem->>'Name' AS item_name,
                       lr."KillCount"
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u ON u."Id" = gc."UserId"
                   , jsonb_array_elements(lr."DropsJson") AS drop_elem
                WHERE lr."SourceName" = @source
                  AND {VisibilityFilter}
                  AND (drop_elem->>'IsFirstTime')::boolean = true
                  AND EXISTS (
                      SELECT 1 FROM "CollectionLogItems" cli
                      WHERE cli."ItemId" = (drop_elem->>'ItemId')::int
                  )
                ORDER BY lr."OccurredAt" DESC
                LIMIT @limit
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));

            var events = new List<SourceClogEvent>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                events.Add(new SourceClogEvent(
                    CharacterName: reader.GetString(1),
                    GameCharacterId: reader.GetInt32(2),
                    ItemName: reader.GetString(3),
                    OccurredAt: reader.GetFieldValue<DateTimeOffset>(0),
                    KillCount: reader.IsDBNull(4) ? null : reader.GetInt32(4)));
            }

            return events;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get recent collection logs for source {Source}", sourceName);
            throw new RepositoryException("Failed to get source recent collection logs", ex);
        }
    }

    public async Task<List<SourceItemFrequency>> GetItemFrequency(string sourceName, string? term, int limit)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            var normalized = string.IsNullOrWhiteSpace(term) ? null : term.Trim();

            // Per (item, character) drop counts (a drop = a kill that yielded the item),
            // grouped into per-item totals + a character breakdown in C#. Optional
            // item-name filter for the in-panel search.
            var sql = $"""
                SELECT drop_elem->>'Name' AS item_name,
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       COUNT(DISTINCT lr."Id")::bigint AS drops
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u ON u."Id" = gc."UserId"
                   , jsonb_array_elements(lr."DropsJson") AS drop_elem
                WHERE lr."SourceName" = @source AND {VisibilityFilter}
                  AND (@term IS NULL OR drop_elem->>'Name' ILIKE '%' || @term || '%')
                GROUP BY drop_elem->>'Name', gc."Id", gc."DisplayName", u."FirstName", u."LastName"
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
            cmd.Parameters.Add(new NpgsqlParameter("@term", NpgsqlTypes.NpgsqlDbType.Text)
            {
                Value = (object?)normalized ?? DBNull.Value
            });

            var rows = new List<(string Item, string Character, long Drops)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));

            return rows
                .GroupBy(r => r.Item)
                .Select(g => new SourceItemFrequency(
                    g.Key,
                    g.Sum(x => x.Drops),
                    g.OrderByDescending(x => x.Drops)
                        .Select(x => new SourceItemCharacterCount(x.Character, x.Drops))
                        .ToList()))
                .OrderByDescending(i => i.TotalDrops)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get item frequency for source {Source}", sourceName);
            throw new RepositoryException("Failed to get source item frequency", ex);
        }
    }

    public async Task<List<SourceTrendPoint>> GetMonthlyTrend(string sourceName)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            // Monthly kills + gp across all visible players, bucketed by Europe/London
            // date to match the per-character trend's timezone handling.
            var totalsSql = $"""
                SELECT EXTRACT(year FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS y,
                       EXTRACT(month FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS m,
                       COUNT(*)::bigint AS kills,
                       SUM(lr."TotalValue")::bigint AS val
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                WHERE lr."SourceName" = @source AND {VisibilityFilter}
                GROUP BY 1, 2
                ORDER BY y, m
                """;

            var totals = new List<(int Y, int M, long Kills, long Value)>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = totalsSql;
                cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    totals.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2),
                        reader.IsDBNull(3) ? 0 : reader.GetInt64(3)));
            }

            // Per (month, character) kills for the hover breakdown.
            var charSql = $"""
                SELECT EXTRACT(year FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS y,
                       EXTRACT(month FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS m,
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       COUNT(*)::bigint AS kills
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u ON u."Id" = gc."UserId"
                WHERE lr."SourceName" = @source AND {VisibilityFilter}
                GROUP BY 1, 2, 3
                ORDER BY y, m, kills DESC
                """;

            // Per (month, character) collection-log item receipts at this source, including
            // duplicates, each annotated with the KC it dropped at — drives the
            // per-character hover detail. (Unlike "Recent collection logs", this is not
            // limited to first-time unlocks.)
            var clogSql = $"""
                SELECT EXTRACT(year FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS y,
                       EXTRACT(month FROM ((lr."OccurredAt" AT TIME ZONE 'Europe/London')::date))::int AS m,
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       drop_elem->>'Name' AS item_name,
                       lr."KillCount"
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u ON u."Id" = gc."UserId"
                   , jsonb_array_elements(lr."DropsJson") AS drop_elem
                WHERE lr."SourceName" = @source AND {VisibilityFilter}
                  AND EXISTS (
                      SELECT 1 FROM "CollectionLogItems" cli
                      WHERE cli."ItemId" = (drop_elem->>'ItemId')::int
                  )
                ORDER BY lr."OccurredAt"
                """;

            var clogsByMonthChar = new Dictionary<(int, int, string), List<SourceTrendClog>>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = clogSql;
                cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var key = (reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2));
                    if (!clogsByMonthChar.TryGetValue(key, out var list))
                    {
                        list = [];
                        clogsByMonthChar[key] = list;
                    }
                    list.Add(new SourceTrendClog(reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4)));
                }
            }

            var byMonth = new Dictionary<(int, int), List<SourceTrendCharacter>>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = charSql;
                cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var y = reader.GetInt32(0);
                    var m = reader.GetInt32(1);
                    var name = reader.GetString(2);
                    if (!byMonth.TryGetValue((y, m), out var list))
                    {
                        list = [];
                        byMonth[(y, m)] = list;
                    }
                    var clogs = clogsByMonthChar.TryGetValue((y, m, name), out var cl) ? cl : [];
                    list.Add(new SourceTrendCharacter(name, reader.GetInt64(3), clogs));
                }
            }

            return totals
                .Select(t => new SourceTrendPoint(
                    t.Y, t.M, t.Kills, t.Value,
                    byMonth.TryGetValue((t.Y, t.M), out var chars) ? chars : []))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get monthly trend for source {Source}", sourceName);
            throw new RepositoryException("Failed to get source monthly trend", ex);
        }
    }
}
