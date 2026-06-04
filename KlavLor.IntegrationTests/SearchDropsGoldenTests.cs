using KlavLor.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KlavLor.IntegrationTests;

// Golden test for the Phase-4d repoint: the new LootDrops-based item search must return
// exactly the same aggregated rows as the original DropsJson unroll. We run both SQLs
// against the same seeded data and assert equality, so the repoint is provably safe.
[Collection("postgres")]
public sealed class SearchDropsGoldenTests(PostgresFixture fx)
{
    private const string OldJsonbSql = """
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
        )
        SELECT u.item_name, SUM(u.qty)::bigint AS total_qty, SUM(u.value)::bigint AS total_value,
               COUNT(DISTINCT u.source_name)::int AS source_count
        FROM unrolled u GROUP BY u.item_name ORDER BY total_value DESC NULLS LAST, u.item_name
        """;

    private const string NewLootDropsSql = """
        WITH unrolled AS (
            SELECT ld."Name" AS item_name,
                   lr."SourceName" AS source_name,
                   ld."Quantity"::bigint AS qty,
                   (ld."Quantity"::bigint * ld."Price"::bigint) AS value
            FROM "LootDrops" ld
            JOIN "LootRecords" lr ON lr."Id" = ld."LootRecordId"
            JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
            WHERE gc."IsVisible" = true AND gc."IsAdminHidden" = false
              AND ld."Name" ILIKE '%' || @term || '%'
        )
        SELECT u.item_name, SUM(u.qty)::bigint AS total_qty, SUM(u.value)::bigint AS total_value,
               COUNT(DISTINCT u.source_name)::int AS source_count
        FROM unrolled u GROUP BY u.item_name ORDER BY total_value DESC NULLS LAST, u.item_name
        """;

    [Fact]
    public async Task New_LootDrops_search_matches_the_old_JSONB_search()
    {
        await using var ctx = fx.CreateContext();
        var (userId, charId) = await Seed.UserAndCharacter(ctx, "golden");
        // A hidden character whose drops must be excluded by both queries.
        var (hiddenUser, hiddenChar) = await Seed.UserAndCharacter(ctx, "goldenhidden", hidden: true);

        var at = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        Seed.AddKill(ctx, userId, charId, "GS_Vorkath", at, 1, [new("Dragon dagger", 1215, 1, 17_000), new("Dragon bones", 536, 50, 300)]);
        Seed.AddKill(ctx, userId, charId, "GS_Zulrah", at.AddDays(1), 2, [new("Dragon dagger", 1215, 2, 17_000)]);
        Seed.AddKill(ctx, userId, charId, "GS_Vorkath", at.AddDays(2), 3, [new("Coins", 995, 5000, 1)]);
        Seed.AddKill(ctx, hiddenUser, hiddenChar, "GS_Vorkath", at.AddDays(3), 1, [new("Dragon dagger", 1215, 99, 17_000)]);
        await ctx.SaveChangesAsync();

        var oldRows = await RunSearch(OldJsonbSql, "dragon");
        var newRows = await RunSearch(NewLootDropsSql, "dragon");

        Assert.NotEmpty(newRows);
        Assert.Equal(oldRows, newRows);
        // Sanity: the hidden character's 99 daggers are excluded (total qty for dagger = 1 + 2).
        Assert.Contains(newRows, r => r.Item == "Dragon dagger" && r.Qty == 3 && r.Sources == 2);
    }

    private async Task<List<(string Item, long Qty, long Value, int Sources)>> RunSearch(string sql, string term)
    {
        await using var ctx = fx.CreateContext();
        var connection = ctx.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@term", term));

        var rows = new List<(string, long, long, int)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3)));
        return rows;
    }
}
