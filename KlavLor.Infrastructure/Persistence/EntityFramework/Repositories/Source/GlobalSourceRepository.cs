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
    // Only visible, non-admin-hidden characters contribute — same rule as the public
    // drop-log grid (GetCharactersWithLoot) and the live feed.
    private const string VisibilityFilter = """gc."IsVisible" = true AND gc."IsAdminHidden" = false""";

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
        => QueryDrops(sourceName, term: null, limit);

    public Task<List<GlobalSourceDrop>> SearchDrops(string sourceName, string? term, int limit)
        => QueryDrops(sourceName, string.IsNullOrWhiteSpace(term) ? null : term.Trim(), limit);

    // Aggregate item drops across all visible characters at this source, optionally
    // filtered by an item-name term. CTE aggregates before the DropRates join so the
    // join can't fan out the sums (same shape as LootLogRepository.GetTopDropsForSource).
    private async Task<List<GlobalSourceDrop>> QueryDrops(string sourceName, string? term, int limit)
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
                      AND (@term IS NULL OR drop_elem->>'Name' ILIKE '%' || @term || '%')
                    GROUP BY drop_elem->>'Name'
                )
                SELECT a.item_name, a.total_qty, a.total_value,
                       dr."Rarity", dr."RarityNumerator", dr."RarityDenominator"
                FROM agg a
                LEFT JOIN "DropRates" dr
                    ON dr."SourceName" = @source
                   AND lower(dr."ItemName") = lower(a.item_name)
                ORDER BY a.total_value DESC NULLS LAST
                LIMIT @limit
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
            cmd.Parameters.Add(new NpgsqlParameter("@term", (object?)term ?? DBNull.Value));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));

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

    public async Task<GlobalSourceCoverage> GetCollectionCoverage(string sourceName)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            // Denominator: clog items whose Tabs array contains this source.
            int total;
            await using (var totalCmd = connection.CreateCommand())
            {
                totalCmd.CommandText = """
                    SELECT COUNT(*)::int FROM "CollectionLogItems"
                    WHERE @source = ANY ("Tabs")
                    """;
                totalCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                var raw = await totalCmd.ExecuteScalarAsync();
                total = raw is null or DBNull ? 0 : Convert.ToInt32(raw);
            }

            // Numerator: distinct clog items dropped from this source by any visible character.
            int unlocked;
            await using (var unlockedCmd = connection.CreateCommand())
            {
                unlockedCmd.CommandText = $"""
                    SELECT COUNT(DISTINCT drop_elem->>'Name')::int
                    FROM "LootRecords" lr
                    JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                       , jsonb_array_elements(lr."DropsJson") AS drop_elem
                    WHERE lr."SourceName" = @source
                      AND {VisibilityFilter}
                      AND EXISTS (
                          SELECT 1 FROM "CollectionLogItems" cli
                          WHERE cli."ItemId" = (drop_elem->>'ItemId')::int
                            AND @source = ANY (cli."Tabs")
                      )
                    """;
                unlockedCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                var raw = await unlockedCmd.ExecuteScalarAsync();
                unlocked = raw is null or DBNull ? 0 : Convert.ToInt32(raw);
            }

            return new GlobalSourceCoverage(unlocked, total);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get collection coverage for source {Source}", sourceName);
            throw new RepositoryException("Failed to get source collection coverage", ex);
        }
    }
}
