using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Loot.Superiors;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class SuperiorSlayerRepository(
    DataContext dataContext,
    ILogger<SuperiorSlayerRepository> logger) : ISuperiorSlayerRepository
{
    // Same predicate as the global source and drop pages. Superior kills are main-game slayer;
    // Leagues loot lives in its own feed scope and must not bleed into a comparison of main accounts.
    private const string VisibilityFilter =
        """gc."IsVisible" = true AND gc."IsAdminHidden" = false AND gc."IsLeagues" = false""";

    // Lowercased on both sides: the wiki's article titles, the wiki's summary table and RuneLite
    // disagree on case for about a third of the list, and a case-sensitive match would split one
    // monster's kills across rows or drop it entirely.
    private const string SuperiorSourceFilter = """lower(lr."SourceName") = ANY(@names)""";

    public async Task<List<SuperiorCountRow>> GetCounts(IReadOnlyCollection<string> loweredSourceNames)
    {
        if (loweredSourceNames.Count == 0) return [];

        try
        {
            // GREATEST(reported, tracked + baseline) is the canonical kill count, matching
            // LootSourceDetailRepository.GetSourceCollection. Both halves are near-always moot for a
            // superior - RuneLite reports no KC for them and nobody sets an admin baseline on a
            // Crushing hand - but using the same definition is what guarantees this page can never
            // quote a different number from the character source page for the same monster.
            const string sql = $"""
                SELECT gc."Id",
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       u."FirstName" || ' ' || u."LastName"                             AS user_name,
                       lower(lr."SourceName")                                           AS source_key,
                       GREATEST(COALESCE(MAX(lr."KillCount"), 0),
                                COUNT(*)::int + COALESCE(bl."BaselineKc", 0))::bigint   AS kills,
                       MIN(lr."OccurredAt")                                             AS first_killed,
                       MAX(lr."OccurredAt")                                             AS last_killed
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u           ON u."Id"  = gc."UserId"
                LEFT JOIN "CharacterSourceBaselines" bl
                       ON bl."GameCharacterId" = gc."Id" AND bl."SourceName" = lr."SourceName"
                WHERE {SuperiorSourceFilter}
                  AND {VisibilityFilter}
                GROUP BY gc."Id", gc."DisplayName", u."FirstName", u."LastName",
                         lower(lr."SourceName"), bl."BaselineKc"
                """;

            await using var cmd = await CreateCommand(sql);
            cmd.Parameters.Add(Names(loweredSourceNames));

            var rows = new List<SuperiorCountRow>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new SuperiorCountRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetFieldValue<DateTimeOffset>(6)));
            }

            return rows;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get superior slayer counts");
            throw new RepositoryException("Failed to get superior slayer counts", ex);
        }
    }

    public async Task<List<SuperiorBaseKillRow>> GetBaseMonsterKills(
        IReadOnlyCollection<string> loweredBaseNames)
    {
        if (loweredBaseNames.Count == 0) return [];

        try
        {
            // Tracked records UNIONed with admin baselines, summed per (character, monster). A plain
            // LEFT JOIN from LootRecords would miss a base monster a character has only a baseline
            // for - and for an ordinary slayer monster that is the common case, since the baseline is
            // exactly the mechanism for "kills we know happened before tracking".
            //
            // Grouped by character, never rolled up across the roster: the page shows each player's
            // own base grind beside their own superior count, and one shared figure could not say
            // whose grind it was.
            const string sql = $"""
                SELECT t.cid, t.name, SUM(t.kills)::bigint
                FROM (
                    SELECT gc."Id"                AS cid,
                           lower(lr."SourceName") AS name,
                           COUNT(*)::bigint       AS kills
                    FROM "LootRecords" lr
                    JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                    WHERE lower(lr."SourceName") = ANY(@names)
                      AND {VisibilityFilter}
                    GROUP BY gc."Id", lower(lr."SourceName")

                    UNION ALL

                    SELECT gc."Id", lower(bl."SourceName"), bl."BaselineKc"::bigint
                    FROM "CharacterSourceBaselines" bl
                    JOIN "GameCharacters" gc ON gc."Id" = bl."GameCharacterId"
                    WHERE lower(bl."SourceName") = ANY(@names)
                      AND {VisibilityFilter}
                ) t
                GROUP BY t.cid, t.name
                """;

            await using var cmd = await CreateCommand(sql);
            cmd.Parameters.Add(Names(loweredBaseNames));

            var rows = new List<SuperiorBaseKillRow>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(new SuperiorBaseKillRow(reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2)));

            return rows;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get base monster kills for the superior comparison");
            throw new RepositoryException("Failed to get base monster kills", ex);
        }
    }

    public async Task<List<SuperiorWeekRow>> GetWeeklyActivity(
        IReadOnlyCollection<string> loweredSourceNames, int weeks)
    {
        if (loweredSourceNames.Count == 0 || weeks <= 0) return [];

        try
        {
            // Tracked records ONLY, and no baselines. A baseline is a lump sum with no date on it -
            // an admin's "they had about this many before we started" - so it cannot be placed on a
            // week, and adding it to one would invent a spike that never happened. This is the one
            // read on this page where that distinction matters, because it is the only one that
            // asks WHEN.
            //
            // date_trunc to the week in UTC, matching how every other timestamp on the site is
            // grouped. The window is trailing and computed here rather than passed as a date, so a
            // cached page cannot pin the axis to the moment the cache was filled.
            const string sql = $"""
                SELECT lower(lr."SourceName")                      AS source_key,
                       date_trunc('week', lr."OccurredAt" AT TIME ZONE 'UTC') AS week_start,
                       COUNT(*)::int                               AS kills
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                WHERE {SuperiorSourceFilter}
                  AND {VisibilityFilter}
                  AND lr."OccurredAt" >= date_trunc('week', now() AT TIME ZONE 'UTC')
                                       - make_interval(weeks => @weeks - 1)
                GROUP BY 1, 2
                """;

            await using var cmd = await CreateCommand(sql);
            cmd.Parameters.Add(Names(loweredSourceNames));
            cmd.Parameters.Add(new NpgsqlParameter("@weeks", weeks));

            var rows = new List<SuperiorWeekRow>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new SuperiorWeekRow(
                    reader.GetString(0),
                    new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)),
                    reader.GetInt32(2)));
            }

            return rows;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get superior weekly activity");
            throw new RepositoryException("Failed to get superior weekly activity", ex);
        }
    }

    public async Task<List<SuperiorUniqueDrop>> GetUniqueDrops(
        IReadOnlyCollection<string> loweredSourceNames,
        IReadOnlyCollection<string> loweredItemNames)
    {
        if (loweredSourceNames.Count == 0 || loweredItemNames.Count == 0) return [];

        try
        {
            // One row per receipt, not an aggregate: a couple of dozen exist in total and each one
            // is an event worth naming - which monster, whose, and when. Rolling them up would throw
            // away the only part anybody would repeat out loud.
            //
            // LootDrops rather than DropsJson: the projection is already per item and indexed, and
            // this reads no price, so none of the effective-price rules apply.
            const string sql = $"""
                SELECT lower(lr."SourceName") AS source_key,
                       ld."Name"              AS item_name,
                       gc."Id"                AS character_id,
                       COALESCE(gc."DisplayName", u."FirstName" || ' ' || u."LastName") AS character_name,
                       lr."OccurredAt"        AS occurred_at
                FROM "LootDrops" ld
                JOIN "LootRecords" lr    ON lr."Id" = ld."LootRecordId"
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                JOIN "Users" u           ON u."Id"  = gc."UserId"
                WHERE {SuperiorSourceFilter}
                  AND lower(ld."Name") = ANY(@items)
                  AND {VisibilityFilter}
                ORDER BY lr."OccurredAt"
                """;

            await using var cmd = await CreateCommand(sql);
            cmd.Parameters.Add(Names(loweredSourceNames));
            cmd.Parameters.Add(new NpgsqlParameter("@items", loweredItemNames.ToArray()));

            var rows = new List<SuperiorUniqueDrop>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new SuperiorUniqueDrop(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }

            return rows;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get superior unique drops");
            throw new RepositoryException("Failed to get superior unique drops", ex);
        }
    }

    private static NpgsqlParameter Names(IReadOnlyCollection<string> loweredSourceNames) =>
        new("@names", loweredSourceNames.ToArray());

    private async Task<NpgsqlCommand> CreateCommand(string sql)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var cmd = (NpgsqlCommand)connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }
}
