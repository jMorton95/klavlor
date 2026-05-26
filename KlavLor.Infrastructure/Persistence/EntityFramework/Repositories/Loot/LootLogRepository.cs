using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class LootLogRepository(DataContext dataContext, ILogger<LootLogRepository> logger)
    : ILootLogRepository
{
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

        var sql = $"""
            SELECT drop_elem->>'Name' as "Name",
                   SUM((drop_elem->>'Quantity')::bigint) as "TotalQuantity",
                   SUM((drop_elem->>'Quantity')::bigint * (drop_elem->>'Price')::bigint) as "TotalValue"
            FROM "LootRecords" lr,
                 jsonb_array_elements(lr."DropsJson") AS drop_elem
            WHERE lr."GameCharacterId" = @characterId
              AND lr."SourceName" = @sourceName
            GROUP BY drop_elem->>'Name'
            ORDER BY "TotalValue" DESC
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
                reader.GetInt64(2)));
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
                   drop_elem->>'Name' as "ItemName",
                   SUM((drop_elem->>'Quantity')::bigint) as "TotalQuantity",
                   SUM((drop_elem->>'Quantity')::bigint * (drop_elem->>'Price')::bigint) as "TotalItemValue"
            FROM "LootRecords" lr,
                 jsonb_array_elements(lr."DropsJson") AS drop_elem
            WHERE lr."GameCharacterId" = @characterId
              AND drop_elem->>'Name' ILIKE '%' || @searchTerm || '%'
              AND lr."SourceName" NOT ILIKE '%' || @searchTerm || '%'
            GROUP BY lr."SourceName", lr."SourceType", drop_elem->>'Name'
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
                return new LootSourceDetail(sourceName, LootSourceType.Unknown, 0, 0, [], [], false);

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
                        drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime))
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
                notableDrops);
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

    public async Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> GetAllFeedTiers(
        int countPerTier, IReadOnlySet<LootFeedTier>? requestedTiers = null)
    {
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
                .Where(x => x.Character.IsVisible && !x.Character.IsAdminHidden)
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

                    var groups = CollapseProjections(candidates, tier, tierMin, tierMax, countPerTier);

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
        int targetGroups)
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
                .Select(d => new LootFeedDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime))
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

            if (groups.Count >= targetGroups) break;
        }
        return groups;
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
            var q = dataContext.LootRecords.Where(r => r.GameCharacterId == characterId);
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

            // Active hours = distinct truncated-hour buckets. Cheap approximation
            // of "time spent earning" without session stitching.
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
        return result is long l ? l : 0;
    }

    private async Task<int> GetNewItemsInWindow(int characterId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        var sql = """
            SELECT COUNT(DISTINCT drop_elem->>'Name')::int
            FROM "LootRecords" lr,
                 jsonb_array_elements(lr."DropsJson") AS drop_elem
            WHERE lr."GameCharacterId" = @cid
              AND (drop_elem->>'IsFirstTime')::boolean = true
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
            // day across two cells in the heatmap.
            const string sql = """
                SELECT (("OccurredAt" AT TIME ZONE 'Europe/London')::date) AS day,
                       COUNT(*)::int AS kills,
                       SUM("TotalValue")::bigint AS gp
                FROM "LootRecords"
                WHERE "GameCharacterId" = @cid
                  AND "OccurredAt" >= @from
                  AND "OccurredAt" < @to
                GROUP BY 1
                ORDER BY 1
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
                    reader.IsDBNull(2) ? 0 : reader.GetInt64(2)));
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get activity calendar for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get activity calendar", ex);
        }
    }

    public async Task<PersonalRecords> GetPersonalRecords(int characterId)
    {
        try
        {
            // Biggest single-kill (covered by IX_LootRecords_GameCharacterId_TotalValue_OccurredAt).
            var topKillRaw = await dataContext.LootRecords
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
            SELECT drop_elem->>'Name' AS item_name,
                   (drop_elem->>'Quantity')::int AS qty,
                   ((drop_elem->>'Quantity')::bigint * (drop_elem->>'Price')::bigint) AS value,
                   lr."SourceName",
                   lr."OccurredAt"
            FROM "LootRecords" lr,
                 jsonb_array_elements(lr."DropsJson") AS drop_elem
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

    public async Task<Dictionary<string, int>> GetDryStreaks(int characterId, IReadOnlyList<string> sourceNames)
    {
        if (sourceNames.Count == 0) return [];
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // CTE: per source, the timestamp of the most recent first-time receipt.
            // Then count kills since that timestamp; if NULL, count all kills.
            const string sql = """
                WITH last_first AS (
                    SELECT lr."SourceName", MAX(lr."OccurredAt") AS last_at
                    FROM "LootRecords" lr, jsonb_array_elements(lr."DropsJson") AS d
                    WHERE lr."GameCharacterId" = @cid
                      AND (d->>'IsFirstTime')::boolean = true
                      AND lr."SourceName" = ANY(@names)
                    GROUP BY lr."SourceName"
                )
                SELECT lr."SourceName",
                       COUNT(*) FILTER (WHERE lr."OccurredAt" > COALESCE(lf.last_at, '-infinity'::timestamptz))::int AS dry
                FROM "LootRecords" lr
                LEFT JOIN last_first lf ON lf."SourceName" = lr."SourceName"
                WHERE lr."GameCharacterId" = @cid
                  AND lr."SourceName" = ANY(@names)
                GROUP BY lr."SourceName"
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@names", sourceNames.ToArray()));

            var result = new Dictionary<string, int>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result[reader.GetString(0)] = reader.GetInt32(1);
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get dry streaks for character {CharacterId}", characterId);
            throw new RepositoryException("Failed to get dry streaks", ex);
        }
    }

    public async Task<SourceCollection> GetSourceCollection(int characterId, string sourceName)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            const string sql = """
                SELECT drop_elem->>'Name' AS item_name,
                       MIN(lr."OccurredAt") AS first_received,
                       SUM((drop_elem->>'Quantity')::bigint) AS qty,
                       SUM((drop_elem->>'Quantity')::bigint * (drop_elem->>'Price')::bigint) AS value,
                       bool_or((drop_elem->>'IsFirstTime')::boolean) AS has_first
                FROM "LootRecords" lr,
                     jsonb_array_elements(lr."DropsJson") AS drop_elem
                WHERE lr."GameCharacterId" = @cid AND lr."SourceName" = @source
                GROUP BY drop_elem->>'Name'
                ORDER BY first_received ASC
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));

            var entries = new List<CollectionEntry>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new CollectionEntry(
                    reader.GetString(0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.GetInt64(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                    !reader.IsDBNull(4) && reader.GetBoolean(4)));
            }
            return new SourceCollection(sourceName, entries);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source collection for character {CharacterId}, source {Source}", characterId, sourceName);
            throw new RepositoryException("Failed to get source collection", ex);
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
            const string sql = """
                SELECT lr."OccurredAt",
                       lr."SourceName",
                       lr."SourceType"::text,
                       drop_elem->>'Name' AS item_name,
                       (drop_elem->>'Quantity')::int AS qty,
                       ((drop_elem->>'Quantity')::bigint * (drop_elem->>'Price')::bigint) AS value
                FROM "LootRecords" lr,
                     jsonb_array_elements(lr."DropsJson") AS drop_elem
                WHERE lr."GameCharacterId" = @cid
                  AND (drop_elem->>'IsFirstTime')::boolean = true
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
                    reader.IsDBNull(5) ? 0 : reader.GetInt64(5)));
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
                           d->>'Name' AS item_name,
                           (d->>'Quantity')::bigint AS qty,
                           ((d->>'Quantity')::bigint * (d->>'Price')::bigint) AS value,
                           (d->>'IsFirstTime')::boolean AS first_time
                    FROM "LootRecords" lr,
                         jsonb_array_elements(lr."DropsJson") AS d
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
