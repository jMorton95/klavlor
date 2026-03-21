using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class LootLogRepository(DataContext dataContext, ILogger<LootLogRepository> logger)
    : ILootLogRepository
{
    public async Task<List<LootLogUserSummary>> GetUsersWithLoot()
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            const string sql = """
                SELECT lr."UserId",
                       u."FirstName" || ' ' || u."LastName" as "UserName",
                       COUNT(DISTINCT lr."SourceName")::int as "TotalSources",
                       COUNT(*)::bigint as "TotalKills",
                       SUM(lr."TotalValue") as "TotalValue"
                FROM "LootRecords" lr
                JOIN "Users" u ON u."Id" = lr."UserId"
                GROUP BY lr."UserId", u."FirstName", u."LastName"
                ORDER BY "TotalValue" DESC
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;

            var users = new List<LootLogUserSummary>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                users.Add(new LootLogUserSummary(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4)));
            }

            return users;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get users with loot");
            throw new RepositoryException("Failed to get users with loot", ex);
        }
    }

    public async Task<LootLogSearchResult> SearchLootLog(int userId, LootLogQuery query)
    {
        try
        {
            var baseQuery = dataContext.LootRecords
                .Where(r => r.UserId == userId);

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm;
                baseQuery = baseQuery.Where(r => EF.Functions.ILike(r.SourceName, $"%{term}%"));
            }

            var totalCount = await baseQuery
                .GroupBy(r => new { r.SourceName, r.SourceType })
                .CountAsync();

            var sourceMatchesRaw = await GetSourceMatches(userId, query, fetchExtra: true);
            var hasMore = sourceMatchesRaw.Count > query.PageSize;
            var sourceMatches = sourceMatchesRaw.Take(query.PageSize).ToList();

            var itemMatches = new List<LootItemMatch>();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var matchedSourceNames = sourceMatches.Select(s => s.SourceName).ToHashSet();
                itemMatches = await GetItemMatches(userId, query.SearchTerm, matchedSourceNames);
            }

            return new LootLogSearchResult(sourceMatches, itemMatches, hasMore, query.SearchTerm, totalCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search loot log for user {UserId}", userId);
            throw new RepositoryException("Failed to search loot log", ex);
        }
    }

    private async Task<List<LootSourceSummary>> GetSourceMatches(int userId, LootLogQuery query, bool fetchExtra = false)
    {
        var baseQuery = dataContext.LootRecords
            .Where(r => r.UserId == userId);

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
            var topDrops = await GetTopDropsForSource(userId, group.SourceName);
            summaries.Add(new LootSourceSummary(
                group.SourceName,
                group.SourceType,
                group.TotalKills,
                group.TotalValue,
                topDrops));
        }

        return summaries;
    }

    private async Task<List<LootDropSummary>> GetTopDropsForSource(int userId, string sourceName, int? limit = 5)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var sql = $"""
            SELECT drop_elem->>'Name' as "Name",
                   SUM((drop_elem->>'Quantity')::bigint) as "TotalQuantity",
                   SUM((drop_elem->>'Quantity')::bigint * (drop_elem->>'Price')::bigint) as "TotalValue"
            FROM "LootRecords" lr,
                 jsonb_array_elements(lr."DropsJson") AS drop_elem
            WHERE lr."UserId" = @userId
              AND lr."SourceName" = @sourceName
            GROUP BY drop_elem->>'Name'
            ORDER BY "TotalValue" DESC
            {(limit.HasValue ? $"LIMIT {limit.Value}" : "")}
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@userId", userId));
        cmd.Parameters.Add(new NpgsqlParameter("@sourceName", sourceName));

        var drops = new List<LootDropSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            drops.Add(new LootDropSummary(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2)));
        }

        return drops;
    }

    private async Task<List<LootItemMatch>> GetItemMatches(
        int userId, string searchTerm, HashSet<string> excludeSourceNames)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        const string sql = """
            SELECT lr."SourceName", lr."SourceType"::text,
                   COUNT(DISTINCT lr."Id") as "TotalKills",
                   drop_elem->>'Name' as "ItemName",
                   SUM((drop_elem->>'Quantity')::bigint) as "TotalQuantity",
                   SUM((drop_elem->>'Quantity')::bigint * (drop_elem->>'Price')::bigint) as "TotalItemValue"
            FROM "LootRecords" lr,
                 jsonb_array_elements(lr."DropsJson") AS drop_elem
            WHERE lr."UserId" = @userId
              AND drop_elem->>'Name' ILIKE '%' || @searchTerm || '%'
              AND lr."SourceName" NOT ILIKE '%' || @searchTerm || '%'
            GROUP BY lr."SourceName", lr."SourceType", drop_elem->>'Name'
            ORDER BY "TotalItemValue" DESC
            LIMIT 50
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@userId", userId));
        cmd.Parameters.Add(new NpgsqlParameter("@searchTerm", searchTerm));

        var items = new List<LootItemMatch>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var sourceName = reader.GetString(0);
            if (excludeSourceNames.Contains(sourceName))
                continue;

            items.Add(new LootItemMatch(
                sourceName,
                reader.GetString(1),
                (int)reader.GetInt64(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5)));
        }

        return items;
    }

    public async Task<LootSourceDetail> GetSourceDetail(int userId, string sourceName, int pageNumber, int pageSize)
    {
        try
        {
            var summary = await dataContext.LootRecords
                .Where(r => r.UserId == userId && r.SourceName == sourceName)
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
                return new LootSourceDetail(sourceName, LootSourceType.Unknown, 0, 0, [], [], false);

            var allDrops = await GetTopDropsForSource(userId, sourceName, limit: null);

            // Notable drops: top 5 kills by value
            var notableRaw = await dataContext.LootRecords
                .Where(r => r.UserId == userId && r.SourceName == sourceName)
                .OrderByDescending(r => r.TotalValue)
                .Take(5)
                .Select(r => new { r.OccurredAt, r.KillCount, r.TotalValue, r.DropsJson })
                .ToListAsync();

            var notableDrops = notableRaw
                .Where(k => k.TotalValue > 0)
                .Select(k =>
                {
                    var drops = JsonSerializer.Deserialize<List<LootDrop>>(k.DropsJson) ?? [];
                    return new LootKillEntry(
                        k.OccurredAt,
                        k.KillCount,
                        k.TotalValue,
                        drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price)).ToList());
                }).ToList();

            var kills = await dataContext.LootRecords
                .Where(r => r.UserId == userId && r.SourceName == sourceName)
                .OrderByDescending(r => r.OccurredAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize + 1)
                .Select(r => new { r.OccurredAt, r.KillCount, r.TotalValue, r.DropsJson })
                .ToListAsync();

            var hasMore = kills.Count > pageSize;
            var killEntries = kills.Take(pageSize).Select(k =>
            {
                var drops = JsonSerializer.Deserialize<List<LootDrop>>(k.DropsJson) ?? [];
                return new LootKillEntry(
                    k.OccurredAt,
                    k.KillCount,
                    k.TotalValue,
                    drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price)).ToList());
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
                notableDrops);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source detail for user {UserId}, source {Source}", userId, sourceName);
            throw new RepositoryException("Failed to get source detail", ex);
        }
    }

    public async Task<LootSourceDetail> GetSourceDetailKillsPage(int userId, string sourceName, int pageNumber, int pageSize)
    {
        try
        {
            var totalKills = await dataContext.LootRecords
                .Where(r => r.UserId == userId && r.SourceName == sourceName)
                .CountAsync();

            var kills = await dataContext.LootRecords
                .Where(r => r.UserId == userId && r.SourceName == sourceName)
                .OrderByDescending(r => r.OccurredAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize + 1)
                .Select(r => new { r.OccurredAt, r.KillCount, r.TotalValue, r.DropsJson })
                .ToListAsync();

            var hasMore = kills.Count > pageSize;
            var killEntries = kills.Take(pageSize).Select(k =>
            {
                var drops = JsonSerializer.Deserialize<List<LootDrop>>(k.DropsJson) ?? [];
                return new LootKillEntry(
                    k.OccurredAt,
                    k.KillCount,
                    k.TotalValue,
                    drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price)).ToList());
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
            logger.LogError(ex, "Failed to get source detail kills page for user {UserId}, source {Source}", userId, sourceName);
            throw new RepositoryException("Failed to get source detail kills page", ex);
        }
    }
}
