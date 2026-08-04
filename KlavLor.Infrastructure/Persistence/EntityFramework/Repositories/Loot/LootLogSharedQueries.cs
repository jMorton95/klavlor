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

// Query helpers shared by more than one of the loot-log repositories, which were split out of the
// single 2,768-line LootLogRepository by consumer feature. These two were private members of that
// class and are used by more than one of the resulting repositories, so they live here rather than
// being duplicated:
//
//   - GetTopDropsForSource: the log search's source matches (LootLogSearchRepository) and both the
//     source-detail page and its popover (LootSourceDetailRepository).
//   - NullableTimestampParam: the profile window/trend queries (LootProfileRepository) and the
//     first-time feed (LootFeedRepository).
//
// The bodies are unchanged from LootLogRepository; GetTopDropsForSource takes the DataContext it
// previously read from its enclosing instance. Consumers import these with `using static`, so the
// call sites read exactly as they did before.
internal static class LootLogSharedQueries
{
    public static async Task<List<LootDropSummary>> GetTopDropsForSource(
        DataContext dataContext, int characterId, string sourceName, int? limit = 5)
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
                       SUM(ld."Quantity"::bigint * ld."Price"::bigint) as total_value,
                       -- Best SINGLE drop, not the running total: feed-tier classification asks
                       -- "did one of these ever land in a swimlane", so 500 cheap drops summing
                       -- to millions must not read as a legendary.
                       MAX(ld."Quantity"::bigint * ld."Price"::bigint) as best_drop_value
                FROM "LootRecords" lr
                JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                WHERE lr."GameCharacterId" = @characterId
                  AND lr."SourceName" = @sourceName
                GROUP BY ld."Name"
            )
            SELECT a.item_name, a.total_qty, a.total_value, a.best_drop_value,
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
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(3) ? 0 : reader.GetInt64(3)));
        }

        return drops;
    }

    public static NpgsqlParameter NullableTimestampParam(string name, DateTimeOffset? value) =>
        new(name, NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = (object?)value ?? DBNull.Value
        };
}
