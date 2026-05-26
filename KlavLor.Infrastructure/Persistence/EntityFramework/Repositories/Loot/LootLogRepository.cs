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
                        drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price))
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
                    drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price))
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
                    drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price))
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
                            KillCount = x.Record.KillCount
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
                .Select(d => new LootFeedDrop(d.Name, d.Quantity, d.Price))
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
                MaxKillCount: r.KillCount);

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
    }
}
