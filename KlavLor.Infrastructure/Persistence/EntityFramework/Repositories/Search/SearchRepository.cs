using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Search;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Search;

internal sealed class SearchRepository(DataContext dataContext, ILogger<SearchRepository> logger) : ISearchRepository
{
    public async Task<List<SearchCharacterResult>> SearchCharacters(string term, int limit)
    {
        try
        {
            // Match on display name, RuneLite id, or owner name; respect the same
            // visibility flags as the public drop-log grid. Loot stats come from
            // correlated aggregates — cheap because the match set (and `limit`) is tiny.
            var pattern = $"%{term}%";

            var rows = await dataContext.GameCharacters
                .AsNoTracking()
                .Where(c => c.IsVisible && !c.IsAdminHidden)
                .Where(c => EF.Functions.ILike(c.DisplayName!, pattern)
                            || EF.Functions.ILike(c.RuneLiteId, pattern)
                            || EF.Functions.ILike(c.User!.FirstName + " " + c.User.LastName, pattern))
                .Select(c => new
                {
                    c.Id,
                    DisplayName = c.DisplayName,
                    UserName = c.User!.FirstName + " " + c.User.LastName,
                    Kills = (long)dataContext.LootRecords.Count(r => r.GameCharacterId == c.Id),
                    Value = dataContext.LootRecords
                        .Where(r => r.GameCharacterId == c.Id)
                        .Sum(r => (long?)r.TotalValue) ?? 0L,
                    Sources = dataContext.LootRecords
                        .Where(r => r.GameCharacterId == c.Id)
                        .Select(r => r.SourceName)
                        .Distinct()
                        .Count()
                })
                .OrderByDescending(c => c.Value)
                .ThenByDescending(c => c.Kills)
                .Take(limit)
                .ToListAsync();

            return rows
                .Select(c => new SearchCharacterResult(
                    c.Id,
                    c.DisplayName ?? c.UserName,
                    c.UserName,
                    c.Sources,
                    c.Kills,
                    c.Value))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search characters for term {Term}", term);
            throw new RepositoryException("Failed to search characters", ex);
        }
    }

    public async Task<List<SearchSourceResult>> SearchSources(string term, int limit)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            // Distinct source names matching the term, aggregated across *all* players.
            // SourceType is persisted as its string label (HasConversion<string>).
            const string sql = """
                SELECT lr."SourceName",
                       lr."SourceType",
                       COUNT(*)::bigint AS total_kills,
                       SUM(lr."TotalValue")::bigint AS total_value
                FROM "LootRecords" lr
                WHERE lr."SourceName" ILIKE '%' || @term || '%'
                GROUP BY lr."SourceName", lr."SourceType"
                ORDER BY total_value DESC NULLS LAST
                LIMIT @limit
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@term", term));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));

            var results = new List<SearchSourceResult>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sourceType = Enum.TryParse<LootSourceType>(reader.GetString(1), ignoreCase: true, out var st)
                    ? st
                    : LootSourceType.Unknown;

                results.Add(new SearchSourceResult(
                    reader.GetString(0),
                    sourceType,
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt64(3)));
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search sources for term {Term}", term);
            throw new RepositoryException("Failed to search sources", ex);
        }
    }

    public async Task<List<SearchDropResult>> SearchDrops(string term, int limit)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            // Item-name matches inside actual loot (DropsJson), aggregated per item across
            // every source it has dropped from. Visible characters only. This is the
            // heaviest section (full JSONB unroll, no index on extracted names) so it is
            // capped and lazy-loaded last on the search page.
            const string sql = """
                WITH unrolled AS (
                    SELECT d->>'Name' AS item_name,
                           lr."SourceName" AS source_name,
                           (d->>'Quantity')::bigint AS qty,
                           ((d->>'Quantity')::bigint * (d->>'Price')::bigint) AS value
                    FROM "LootRecords" lr
                    JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                       , jsonb_array_elements(lr."DropsJson") AS d
                    WHERE gc."IsVisible" = true AND gc."IsAdminHidden" = false
                      AND d->>'Name' ILIKE '%' || @term || '%'
                ),
                top_source AS (
                    SELECT item_name, source_name, SUM(value) AS sv,
                           ROW_NUMBER() OVER (PARTITION BY item_name ORDER BY SUM(value) DESC) AS rn
                    FROM unrolled
                    GROUP BY item_name, source_name
                )
                SELECT u.item_name,
                       SUM(u.qty)::bigint AS total_qty,
                       SUM(u.value)::bigint AS total_value,
                       COUNT(DISTINCT u.source_name)::int AS source_count,
                       (SELECT t.source_name FROM top_source t WHERE t.item_name = u.item_name AND t.rn = 1) AS top_source
                FROM unrolled u
                GROUP BY u.item_name
                ORDER BY total_value DESC NULLS LAST
                LIMIT @limit
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@term", term));
            cmd.Parameters.Add(new NpgsqlParameter("@limit", limit));

            var results = new List<SearchDropResult>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new SearchDropResult(
                    reader.GetString(0),
                    reader.GetInt64(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? "" : reader.GetString(4)));
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search drops for term {Term}", term);
            throw new RepositoryException("Failed to search drops", ex);
        }
    }

    public async Task<List<SearchItemResult>> SearchItemCatalog(string term, int limit)
    {
        try
        {
            var pattern = $"%{term}%";

            // Sequential awaits — one scoped DbContext, never concurrent.
            var gear = await dataContext.GearItems
                .AsNoTracking()
                .Where(g => EF.Functions.ILike(g.Name, pattern))
                .OrderBy(g => g.Name)
                .Take(limit)
                .Select(g => new SearchItemResult(g.Name, SearchItemKind.GearItem, g.WikiUrl, g.ImageUrl))
                .ToListAsync();

            var clog = await dataContext.CollectionLogItems
                .AsNoTracking()
                .Where(c => EF.Functions.ILike(c.Name, pattern))
                .OrderBy(c => c.Name)
                .Take(limit)
                .Select(c => new SearchItemResult(c.Name, SearchItemKind.CollectionLogItem, null, null))
                .ToListAsync();

            // Merge, de-duping by name; the richer GearItem (has wiki/image) wins.
            var byName = new Dictionary<string, SearchItemResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in gear) byName[g.Name] = g;
            foreach (var c in clog) byName.TryAdd(c.Name, c);

            return byName.Values
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search item catalog for term {Term}", term);
            throw new RepositoryException("Failed to search item catalog", ex);
        }
    }
}
