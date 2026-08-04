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
using static KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot.LootLogSharedQueries;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

// One character at one source: the detail page and its paged kill list, the hover popover, the
// monthly kill trend, and the collection-log progress panel. Split out of LootLogRepository by
// consumer feature; the queries are unchanged.
internal sealed class LootSourceDetailRepository(
    DataContext dataContext, ILogger<LootSourceDetailRepository> logger, ICollectionLogCache collectionLogCache)
    : ILootSourceDetailRepository
{
    public async Task<LootSourceDetail> GetSourceDetail(int characterId, string sourceName, int pageNumber, int pageSize)
    {
        try
        {
            // Whose log this is — drives the "X's loot from Y" page header. One cheap PK lookup.
            var characterName = await dataContext.GameCharacters
                .Where(c => c.Id == characterId)
                .Select(c => c.DisplayName ?? c.User!.FirstName + " " + c.User.LastName)
                .FirstOrDefaultAsync();

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
                return new LootSourceDetail(sourceName, LootSourceType.Unknown, 0, 0, [], [], false, CharacterName: characterName);

            var allDrops = await GetTopDropsForSource(dataContext, characterId, sourceName, limit: null);

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
                        drops.Select(d => new LootKillDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime, collectionLogCache.IsCollectionLogItem(d.ItemId, d.Name)))
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
                notableDrops,
                characterName);
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

    public async Task<SourceKillTrend> GetSourceKillTrend(int characterId, string sourceName)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // Weekly kills + gp for this character at this source, bucketed by the Europe/London
            // occurrence date to match every other time-bucketed aggregate. date_trunc('week')
            // anchors on Monday, so a "week" here is the ISO week the kill landed in.
            //
            // Every active week is returned, not a recent window: the cumulative series has to
            // begin at the character's real lifetime total even when the panel only draws the
            // last thirteen weeks, and one row per active week is a trivial result set.
            const string sql = """
                SELECT (date_trunc('week', ("OccurredAt" AT TIME ZONE 'Europe/London')::date))::date AS wk,
                       COUNT(*)::int AS kills,
                       SUM("TotalValue")::bigint AS val
                FROM "LootRecords"
                WHERE "GameCharacterId" = @cid AND "SourceName" = @source
                GROUP BY 1
                ORDER BY 1
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));

            var weeks = new List<SourceKillTrendWeek>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                weeks.Add(new SourceKillTrendWeek(
                    DateOnly.FromDateTime(reader.GetFieldValue<DateTime>(0)),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? 0 : reader.GetInt64(2)));
            }

            return new SourceKillTrend(sourceName, weeks);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source kill trend for character {CharacterId} at {Source}", characterId, sourceName);
            throw new RepositoryException("Failed to get source kill trend", ex);
        }
    }

    // Cap on how many recent drop events each collection item carries for its hover popover.
    // Without it, an item received thousands of times fetched and rendered one row per receipt.
    private const int RecentDropEventsPerItem = 50;

    public async Task<SourceCollection> GetSourceCollection(int characterId, string sourceName)
    {
        try
        {
            // Admin baseline: kills done before we had any data for this character. Added to the
            // counted (row-count) KC and ordinal fallbacks — never to a real reported KillCount.
            var baseline = await dataContext.CharacterSourceBaselines
                .Where(b => b.GameCharacterId == characterId && b.SourceName == sourceName)
                .Select(b => (int?)b.BaselineKc)
                .FirstOrDefaultAsync() ?? 0;

            // Every run at this source that carries a derived depth, oldest first. Deliberately
            // the full per-run list and NOT a max/aggregate: depth-modelled luck (Doom) must be
            // computed from the depth each run actually reached, otherwise every shallow run is
            // scored as if it had gone as deep as the player's best ever, which overstates the
            // odds and reports everyone as dry. Empty for ordinary sources (EffectiveKills null).
            // Every claim at this source, with its stored depth where the backfill has derived one
            // and its DropsJson so the handler can derive the rest on read. Deliberately NOT
            // filtered to EffectiveKills != null: gating on that left the whole depth model inert
            // for any character whose records the backfill had not reached, which is how Doom ended
            // up showing a plain run count. The handler discards this for non-depth sources.
            var runs = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName)
                .OrderBy(r => r.OccurredAt).ThenBy(r => r.Id)
                .Select(r => new SourceRun(r.Id, r.OccurredAt, r.EffectiveKills ?? 0, r.DropsJson))
                .ToListAsync();

            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // KillCount/KillOrdinal correspond to the *earliest* LootRecord containing
            // each item — i.e. the KC at which the item first dropped for this character.
            // DISTINCT ON picks that earliest row per item; the correlated subquery
            // computes the chronological ordinal (fallback when RuneLite gave no KC).
            // Restrict to items that are in the real OSRS collection log so the tab
            // matches its name; the in-game "All Drops" panel on LootLogSourceDetail
            // still shows everything regardless of clog status.
            const string sql = """
                WITH unrolled AS (
                    SELECT lr."Id", lr."OccurredAt", lr."KillCount",
                           ld."Name" AS item_name,
                           ld."Quantity"::bigint AS qty,
                           (ld."Quantity"::bigint * ld."Price"::bigint) AS value,
                           ld."IsFirstTime" AS first_time
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid AND lr."SourceName" = @source
                      -- Split into two EXISTS rather than one with an OR inside: the OR blocks the
                      -- ItemId PK index and forces a full clog-view scan per drop. Separate EXISTS
                      -- each use an index (ItemId PK; lower(Name) expression index) and the fast
                      -- id path short-circuits for the common case.
                      AND (EXISTS (SELECT 1 FROM "EffectiveCollectionLogItems" cli WHERE cli."ItemId" = ld."ItemId")
                           OR EXISTS (SELECT 1 FROM "EffectiveCollectionLogItems" cli WHERE lower(cli."Name") = lower(ld."Name")))
                ),
                agg AS (
                    SELECT item_name,
                           MIN("OccurredAt") AS first_received,
                           MAX("OccurredAt") AS last_received,
                           COUNT(*)::int AS total_drops,
                           SUM(qty) AS total_qty,
                           SUM(value) AS total_value,
                           bool_or(first_time) AS has_first
                    FROM unrolled
                    GROUP BY item_name
                ),
                first_row AS (
                    SELECT DISTINCT ON (item_name)
                           item_name, "Id", "OccurredAt", "KillCount"
                    FROM unrolled
                    ORDER BY item_name, "OccurredAt" ASC, "Id" ASC
                ),
                last_row AS (
                    SELECT DISTINCT ON (item_name)
                           item_name, "Id", "OccurredAt", "KillCount"
                    FROM unrolled
                    ORDER BY item_name, "OccurredAt" DESC, "Id" DESC
                )
                SELECT a.item_name, a.first_received, a.last_received, a.total_drops,
                       a.total_qty, a.total_value, a.has_first,
                       f."Id" AS first_record_id,
                       f."KillCount" AS first_kc,
                       ((SELECT COUNT(*)::int FROM "LootRecords" o
                        WHERE o."GameCharacterId" = @cid
                          AND o."SourceName" = @source
                          AND (o."OccurredAt" < f."OccurredAt"
                               OR (o."OccurredAt" = f."OccurredAt" AND o."Id" <= f."Id"))) + @baseline) AS first_ordinal,
                       l."KillCount" AS last_kc,
                       ((SELECT COUNT(*)::int FROM "LootRecords" o
                        WHERE o."GameCharacterId" = @cid
                          AND o."SourceName" = @source
                          AND (o."OccurredAt" < l."OccurredAt"
                               OR (o."OccurredAt" = l."OccurredAt" AND o."Id" <= l."Id"))) + @baseline) AS last_ordinal,
                       dr."Rarity", dr."RarityNumerator", dr."RarityDenominator", dr."Rolls"
                FROM agg a
                JOIN first_row f ON f.item_name = a.item_name
                JOIN last_row l ON l.item_name = a.item_name
                LEFT JOIN "DropRates" dr
                    ON dr."SourceName" = @source
                   AND lower(dr."ItemName") = lower(a.item_name)
                ORDER BY lower(a.item_name) ASC
                """;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
            cmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
            cmd.Parameters.Add(new NpgsqlParameter("@baseline", baseline));

            var entries = new List<CollectionEntry>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    entries.Add(new CollectionEntry(
                        ItemName: reader.GetString(0),
                        FirstReceivedAt: reader.GetFieldValue<DateTimeOffset>(1),
                        LastReceivedAt: reader.GetFieldValue<DateTimeOffset>(2),
                        TotalDrops: reader.GetInt32(3),
                        TotalQuantity: reader.GetInt64(4),
                        TotalValue: reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                        MarkedFirstTime: !reader.IsDBNull(6) && reader.GetBoolean(6),
                        KillCount: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        KillOrdinal: reader.IsDBNull(9) ? null : reader.GetInt32(9),
                        LastKillCount: reader.IsDBNull(10) ? null : reader.GetInt32(10),
                        LastKillOrdinal: reader.IsDBNull(11) ? null : reader.GetInt32(11),
                        Rarity: reader.IsDBNull(12) ? null : reader.GetString(12),
                        RarityNumerator: reader.IsDBNull(13) ? null : reader.GetInt32(13),
                        RarityDenominator: reader.IsDBNull(14) ? null : reader.GetInt32(14),
                        Rolls: reader.IsDBNull(15) ? 1 : reader.GetInt32(15),
                        FirstRecordId: reader.IsDBNull(7) ? 0 : reader.GetInt32(7)));
                }
            }

            // Missing items: every clog entry whose Tabs array contains the source name,
            // minus those the character has already received from this source. Empty when
            // the source has no clog tab mapping (e.g. unrecognised RuneLite source name).
            var missingItems = new List<MissingClogItem>();
            await using (var missingCmd = connection.CreateCommand())
            {
                missingCmd.CommandText = """
                    SELECT cli."Name", dr."Rarity", dr."RarityNumerator", dr."RarityDenominator", dr."Rolls"
                    FROM "EffectiveCollectionLogItems" cli
                    LEFT JOIN "DropRates" dr
                        ON dr."SourceName" = @source
                       AND lower(dr."ItemName") = lower(cli."Name")
                    -- A clog item belongs to this source if the wiki tab mapping says so, OR we
                    -- otherwise have data that it drops here: a stored drop rate for the source.
                    -- The latter surfaces items (e.g. Dragon warhammer from Lizardman shaman) whose
                    -- tab mapping doesn't list the source, so every character sees it consistently.
                    WHERE (@source = ANY (cli."Tabs")
                           OR EXISTS (SELECT 1 FROM "DropRates" dr2
                                      WHERE dr2."SourceName" = @source
                                        AND lower(dr2."ItemName") = lower(cli."Name")))
                      AND NOT EXISTS (
                          SELECT 1 FROM "LootRecords" lr
                          JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                          WHERE lr."GameCharacterId" = @cid
                            AND lr."SourceName" = @source
                            AND (ld."ItemId" = cli."ItemId"
                                 OR lower(ld."Name") = lower(cli."Name"))
                      )
                    ORDER BY lower(cli."Name")
                    """;
                missingCmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                missingCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                await using var missingReader = await missingCmd.ExecuteReaderAsync();
                while (await missingReader.ReadAsync())
                {
                    missingItems.Add(new MissingClogItem(
                        ItemName: missingReader.GetString(0),
                        Rarity: missingReader.IsDBNull(1) ? null : missingReader.GetString(1),
                        RarityNumerator: missingReader.IsDBNull(2) ? null : missingReader.GetInt32(2),
                        RarityDenominator: missingReader.IsDBNull(3) ? null : missingReader.GetInt32(3),
                        Rolls: missingReader.IsDBNull(4) ? 1 : missingReader.GetInt32(4)));
                }
            }

            // Character KC at this source — denominator for luck/expected calcs. Prefer
            // RuneLite's reported KillCount (matches the in-game counter the player sees);
            // fall back to the logged-row count when KillCount was never reported.
            // Using COUNT instead understates progress when the player started syncing
            // partway through their kills, which made luck pills nonsensical for any
            // multi-drop item (e.g. 8 drops shown as "Spooned 30×" because the implicit
            // KC was a fraction of the real in-game value).
            var maxKc = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName && r.KillCount != null && r.KillCount > 0)
                .MaxAsync(r => (int?)r.KillCount);
            var counted = await dataContext.LootRecords
                .CountAsync(r => r.GameCharacterId == characterId && r.SourceName == sourceName);

            // Two scales exist and they must never be mixed. A reported KillCount is the absolute
            // in-game counter (the baseline is already inside it). The fallback — logged rows plus
            // the admin baseline — is our own reconstruction. Per-item ordinals use the
            // reconstruction, so if a character only started reporting KC partway through, taking
            // the raw max would compare an absolute in-game number against reconstructed ordinals
            // for the same source. Take whichever scale is larger: the reported counter is
            // authoritative when it leads, and the reconstruction wins when it has more kills
            // than the counter ever reported.
            var characterKc = Math.Max(maxKc ?? 0, counted + baseline);

            // Per-item drop events. Drives the KC-column hover popover that lists every
            // drop occurrence for an item. Only rows that show up as clog entries get
            // populated, so the join cost is bounded.
            var eventsByItem = new Dictionary<string, List<DropEvent>>(StringComparer.OrdinalIgnoreCase);
            if (entries.Count > 0)
            {
                await using var eventsCmd = connection.CreateCommand();
                // The hover popover only lists the most recent receipts per item — cap at
                // @limit so an item dropped thousands of times doesn't fetch (and render) one
                // row per receipt. Rank within each item first, then compute the chronological
                // ordinal only for the rows we keep. Newest-first so the popover shows the
                // latest drops at the top; the true total stays on CollectionEntry.TotalDrops.
                eventsCmd.CommandText = """
                    WITH ranked AS (
                        SELECT ld."Name" AS item_name, lr."OccurredAt", lr."KillCount", lr."Id",
                               ROW_NUMBER() OVER (PARTITION BY ld."Name"
                                                  ORDER BY lr."OccurredAt" DESC, lr."Id" DESC) AS rn
                        FROM "LootRecords" lr
                        JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                        WHERE lr."GameCharacterId" = @cid AND lr."SourceName" = @source
                          AND EXISTS (
                              SELECT 1 FROM "EffectiveCollectionLogItems" cli
                              WHERE cli."ItemId" = ld."ItemId"
                          )
                    )
                    SELECT r.item_name, r."OccurredAt", r."KillCount",
                           ((SELECT COUNT(*)::int FROM "LootRecords" o
                            WHERE o."GameCharacterId" = @cid
                              AND o."SourceName" = @source
                              AND (o."OccurredAt" < r."OccurredAt"
                                   OR (o."OccurredAt" = r."OccurredAt" AND o."Id" <= r."Id"))) + @baseline) AS kill_ordinal
                    FROM ranked r
                    WHERE r.rn <= @limit
                    ORDER BY r.item_name, r."OccurredAt" DESC, r."Id" DESC
                    """;
                eventsCmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                eventsCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                eventsCmd.Parameters.Add(new NpgsqlParameter("@limit", RecentDropEventsPerItem));
                eventsCmd.Parameters.Add(new NpgsqlParameter("@baseline", baseline));
                await using var eventsReader = await eventsCmd.ExecuteReaderAsync();
                while (await eventsReader.ReadAsync())
                {
                    var name = eventsReader.GetString(0);
                    if (!eventsByItem.TryGetValue(name, out var list))
                    {
                        list = new List<DropEvent>();
                        eventsByItem[name] = list;
                    }
                    list.Add(new DropEvent(
                        eventsReader.GetFieldValue<DateTimeOffset>(1),
                        eventsReader.IsDBNull(2) ? null : eventsReader.GetInt32(2),
                        eventsReader.IsDBNull(3) ? null : eventsReader.GetInt32(3)));
                }

                for (var i = 0; i < entries.Count; i++)
                {
                    if (eventsByItem.TryGetValue(entries[i].ItemName, out var ev))
                        entries[i] = entries[i] with { DropEvents = ev };
                }
            }

            return new SourceCollection(sourceName, characterKc, entries, missingItems, runs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source collection for character {CharacterId}, source {Source}", characterId, sourceName);
            throw new RepositoryException("Failed to get source collection", ex);
        }
    }

    public async Task<SourcePopoverData> GetSourcePopover(int characterId, string sourceName)
    {
        try
        {
            // Summary row: KC + total GP for this character at this source. Empty rows
            // get a zeroed payload so the caller can still render the boss icon/name.
            var summary = await dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId && r.SourceName == sourceName)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalKills = g.Count(),
                    TotalValue = g.Sum(r => r.TotalValue)
                })
                .FirstOrDefaultAsync();

            var topDrops = await GetTopDropsForSource(dataContext, characterId, sourceName, limit: 5);

            // Collection-log progress: numerator = distinct clog items this character has
            // ever received from this source; denominator = clog items whose Tabs array
            // contains the source name. Tabs is wiki-synced and can be unmapped for some
            // sources (e.g. minigames named differently in RuneLite) — when ClogTotal=0
            // the UI degrades to "X clog items" without the fraction.
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            int clogUnlocked;
            await using (var unlockedCmd = connection.CreateCommand())
            {
                unlockedCmd.CommandText = """
                    SELECT COUNT(DISTINCT ld."Name")::int
                    FROM "LootRecords" lr
                    JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                    WHERE lr."GameCharacterId" = @cid
                      AND lr."SourceName" = @source
                      AND EXISTS (
                          SELECT 1 FROM "EffectiveCollectionLogItems" cli
                          WHERE cli."ItemId" = ld."ItemId"
                            AND @source = ANY (cli."Tabs")
                      )
                    """;
                unlockedCmd.Parameters.Add(new NpgsqlParameter("@cid", characterId));
                unlockedCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                var raw = await unlockedCmd.ExecuteScalarAsync();
                clogUnlocked = raw is null or DBNull ? 0 : Convert.ToInt32(raw);
            }

            int clogTotal;
            await using (var totalCmd = connection.CreateCommand())
            {
                totalCmd.CommandText = """
                    SELECT COUNT(*)::int FROM "EffectiveCollectionLogItems"
                    WHERE @source = ANY ("Tabs")
                    """;
                totalCmd.Parameters.Add(new NpgsqlParameter("@source", sourceName));
                var raw = await totalCmd.ExecuteScalarAsync();
                clogTotal = raw is null or DBNull ? 0 : Convert.ToInt32(raw);
            }

            return new SourcePopoverData(
                sourceName,
                summary?.TotalKills ?? 0,
                summary?.TotalValue ?? 0,
                clogUnlocked,
                clogTotal,
                topDrops);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source popover for character {CharacterId}, source {Source}", characterId, sourceName);
            throw new RepositoryException("Failed to get source popover", ex);
        }
    }
}
