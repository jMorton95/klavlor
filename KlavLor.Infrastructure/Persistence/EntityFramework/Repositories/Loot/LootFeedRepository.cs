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

// Everything that builds feed cards from stored records: the live feed's per-tier backfill, the
// character day feed, and the first-time (collection-log) feed. Split out of LootLogRepository by
// consumer feature; the queries and the collapse/expand passes are unchanged.
internal sealed class LootFeedRepository(
    DataContext dataContext, ILogger<LootFeedRepository> logger, ICollectionLogCache collectionLogCache,
    IItemValueOverrideCache itemValues)
    : ILootFeedRepository
{
    public async Task<Dictionary<LootFeedTier, List<LootFeedEntry>>> GetAllFeedTiers(
        int countPerTier, LootFeedScope scope = LootFeedScope.Main, IReadOnlySet<LootFeedTier>? requestedTiers = null)
    {
        var isLeagues = scope == LootFeedScope.Leagues;
        // The cap counts *grouped* entries: adjacent same-source kills collapse into one card
        // (e.g. 10 clues in a row = 1 entry), and we want countPerTier of those, not raw records.
        // Tier classification is per-drop (each LootRecord can split across tiers via its drops),
        // so we over-fetch candidates, filter+collapse in C#, and refetch with a larger window
        // only when one user's run dominated the initial fetch. initialTake is sized for the 16h
        // grouping window, which collapses many raw kills into one card, so the common case fits
        // a single fetch and avoids the doubling re-queries.
        const int initialTake = 300;
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
                .Where(x => x.Character.IsVisible && !x.Character.IsAdminHidden && x.Character.IsLeagues == isLeagues)
                .Join(dataContext.Users, x => x.Character.UserId, u => u.Id, (x, u) => new { x.Record, x.Character, User = u });

            foreach (var tier in tiers)
            {
                var (tierMin, tierMax) = ILootFeedService.GetTierRange(tier);
                // Zero-value special drops never clear the value gate, so pull records that carry
                // one into the top lane explicitly.
                var isLegendaryTier = tier == LootFeedTier.Legendary;
                var take = initialTake;

                while (true)
                {
                    var candidates = await baseQuery
                        .Where(x => x.Record.TotalValue >= tierMin
                                    || (isLegendaryTier && dataContext.LootDrops.Any(d => d.LootRecordId == x.Record.Id && d.IsSpecial)))
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
                            EffectiveKills = x.Record.EffectiveKills
                            // KillOrdinal is intentionally NOT computed per-row here: it's only a
                            // fallback label shown when RuneLite omitted KillCount, so a per-row
                            // correlated count over (up to) hardCap candidates × 5 tiers on every
                            // feed load was wasted work. It's filled lazily below for the handful
                            // of surviving cards that actually need it (FillSurvivorOrdinals).
                        })
                        .ToListAsync();

                    var groups = CollapseProjections(candidates, tier, tierMin, tierMax, countPerTier, collectionLogCache, itemValues, scope);

                    if (groups.Count >= countPerTier || candidates.Count < take || take >= hardCap)
                    {
                        result[tier] = groups;
                        break;
                    }

                    take = Math.Min(take * 2, hardCap);
                }
            }

            var expandedEnds = await ExpandToSessionBounds(result);
            await FillSurvivorOrdinals(result, expandedEnds);
            await FillDropOrdinals(result);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get all feed tiers");
            throw new RepositoryException("Failed to get all feed tiers", ex);
        }
    }

    // A card's kills are only the ones whose drops qualified for its tier, so a range built from
    // them starts at the first qualifying drop rather than the start of the play session. This
    // pass widens each surviving card to the whole session(s) it overlaps: Min/MaxKillCount
    // become the session's min/max reported KC, and GroupStartedAt moves back to the session's
    // first kill. Returns each card's expanded session end so the ordinal fill below can count
    // through kills after the card's last qualifying drop.
    //
    // Sessions here are the SITE-WIDE model (MaxGap/SessionBreakGap), not the tier's merge
    // window, so every swimlane shows the same range for the same play session — and the range
    // matches the character page's session history. The `anchor` CTE finds the true gap-session
    // start before the card so the 16h duration chunks line up with the global computation
    // instead of drifting with the slice edge.
    private async Task<Dictionary<(LootFeedTier Tier, int Index), DateTimeOffset>> ExpandToSessionBounds(
        Dictionary<LootFeedTier, List<LootFeedEntry>> result)
    {
        var slots = new List<(LootFeedTier Tier, int Index)>();
        var cids = new List<int>();
        var srcs = new List<string>();
        var firsts = new List<DateTimeOffset>();
        var lasts = new List<DateTimeOffset>();

        foreach (var (tier, entries) in result)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.GameCharacterId is null) continue;
                slots.Add((tier, i));
                cids.Add(e.GameCharacterId.Value);
                srcs.Add(e.SourceName);
                firsts.Add(e.GroupAnchorAt);
                lasts.Add(e.OccurredAt);
            }
        }

        var expandedEnds = new Dictionary<(LootFeedTier, int), DateTimeOffset>();
        if (slots.Count == 0) return expandedEnds;

        // Per card: `anchor` finds the gap-session start the global model would use — the latest
        // real break (16h gap / 6h overnight) in the last 14 days, else the source's first kill
        // ever (continuous grinders never break, so their 16h chunks count from kill #1). The
        // slice then only needs ±2 windows around the card: chunk numbers are arithmetic from
        // the anchor (or any in-slice break), no full-history scan.
        var sql = $"""
            SELECT t.idx, s.min_kc, s.max_kc, s.start_at, s.end_at
            FROM unnest(@cids, @srcs, @firsts, @lasts) WITH ORDINALITY
                 AS t(cid, src, first, last, idx)
            LEFT JOIN LATERAL (
                WITH anchor AS (
                    SELECT COALESCE(
                        (SELECT max(b."OccurredAt") FROM (
                            SELECT r."OccurredAt",
                                   lag(r."OccurredAt") OVER (ORDER BY r."OccurredAt", r."Id") AS prev_at
                            FROM "LootRecords" r
                            WHERE r."GameCharacterId" = t.cid AND r."SourceName" = t.src
                              AND r."OccurredAt" >= t.first - interval '14 days'
                              AND r."OccurredAt" <= t.first
                        ) b
                        WHERE b.prev_at IS NOT NULL
                          AND ((b."OccurredAt" - b.prev_at) > @gap
                               OR ((b."OccurredAt" - b.prev_at) >= @breakGap
                                   AND date((b."OccurredAt" AT TIME ZONE 'Europe/London') - INTERVAL '6 hours')
                                    <> date((b.prev_at AT TIME ZONE 'Europe/London') - INTERVAL '6 hours')))),
                        (SELECT min(r."OccurredAt") FROM "LootRecords" r
                          WHERE r."GameCharacterId" = t.cid AND r."SourceName" = t.src)
                    ) AS s
                ),
                slice AS (
                    SELECT r."OccurredAt", r."Id", r."KillCount",
                           lag(r."OccurredAt") OVER (ORDER BY r."OccurredAt", r."Id") AS prev_at
                    FROM "LootRecords" r
                    WHERE r."GameCharacterId" = t.cid AND r."SourceName" = t.src
                      AND r."OccurredAt" >= greatest((SELECT a.s FROM anchor a), t.first - @gap * 2)
                      AND r."OccurredAt" <= t.last + @gap * 2
                ),
                marked AS (
                    -- No breaks exist in (anchor, first] by construction, so a NULL prev at the
                    -- slice edge is an artifact, not a session start.
                    SELECT *, CASE WHEN prev_at IS NOT NULL
                                     AND (("OccurredAt" - prev_at) > @gap
                                          OR (("OccurredAt" - prev_at) >= @breakGap
                                              AND date(("OccurredAt" AT TIME ZONE 'Europe/London') - INTERVAL '6 hours')
                                               <> date((prev_at AT TIME ZONE 'Europe/London') - INTERVAL '6 hours')))
                                    THEN 1 ELSE 0 END AS brk
                    FROM slice
                ),
                based AS (
                    SELECT *, COALESCE(max(CASE WHEN brk = 1 THEN "OccurredAt" END)
                                         OVER (ORDER BY "OccurredAt", "Id" ROWS UNBOUNDED PRECEDING),
                                       (SELECT a.s FROM anchor a)) AS base
                    FROM marked
                ),
                chunked AS (
                    SELECT *, floor(extract(epoch FROM ("OccurredAt" - base))
                                    / extract(epoch FROM @gap))::int AS chunk
                    FROM based
                ),
                capped AS (
                    SELECT *, CASE WHEN brk = 1 OR chunk <> lag(chunk) OVER (ORDER BY "OccurredAt", "Id")
                                    THEN 1 ELSE 0 END AS new_sess
                    FROM chunked
                ),
                sessioned AS (
                    SELECT *, SUM(new_sess) OVER (ORDER BY "OccurredAt", "Id") AS session_no
                    FROM capped
                )
                SELECT min(x."KillCount") AS min_kc, max(x."KillCount") AS max_kc,
                       min(x."OccurredAt") AS start_at, max(x."OccurredAt") AS end_at
                FROM sessioned x
                WHERE x.session_no IN (SELECT DISTINCT y.session_no FROM sessioned y
                                       WHERE y."OccurredAt" >= t.first AND y."OccurredAt" <= t.last)
            ) s ON true
            """;

        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@cids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer) { Value = cids.ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter("@srcs", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { Value = srcs.ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter("@firsts", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = firsts.ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter("@lasts", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = lasts.ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter("@gap", NpgsqlTypes.NpgsqlDbType.Interval) { Value = LootFeedGrouping.MaxGap });
        cmd.Parameters.Add(new NpgsqlParameter("@breakGap", NpgsqlTypes.NpgsqlDbType.Interval) { Value = LootFeedGrouping.SessionBreakGap });

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var idx = (int)reader.GetInt64(0) - 1;
            if (reader.IsDBNull(3)) continue; // no kills found — leave the card untouched

            int? minKc = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            int? maxKc = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            var startAt = reader.GetFieldValue<DateTimeOffset>(3);
            var endAt = reader.GetFieldValue<DateTimeOffset>(4);

            var (tier, index) = slots[idx];
            var e = result[tier][index];
            result[tier][index] = e with
            {
                MinKillCount = minKc ?? e.MinKillCount,
                MaxKillCount = maxKc ?? e.MaxKillCount,
                GroupStartedAt = startAt < e.GroupAnchorAt ? startAt : e.GroupAnchorAt
            };
            expandedEnds[(tier, index)] = endAt > e.OccurredAt ? endAt : e.OccurredAt;
        }

        return expandedEnds;
    }

    // KillOrdinal is the absolute chronological position of a kill within its (character, source)
    // history (1 = oldest). It's only rendered as a fallback when RuneLite didn't report an in-game
    // KillCount, so we compute it here — in one batched query, only for the surviving cards that
    // lack a KillCount — rather than per-candidate inside GetAllFeedTiers' over-fetch loop.
    // Resolves the roll number of each INDIVIDUAL drop on a surviving card, for the records that
    // carry no RuneLite kill count. Without this every drop on such a card fell back to the card's
    // own first ordinal: a Lunar Chest card spanning rolls 197-420 reported all four of its uniques
    // at roll 197, so a drop that actually came at 420 read as though it had come on the first roll
    // and looked far luckier than it was.
    //
    // One batched query for the distinct (character, source, timestamp) triples across every drop
    // that needs one, counting rows at-or-before that timestamp. Same counting rule as
    // FillSurvivorOrdinals, including the admin baseline, so a drop's number is on the same scale as
    // the range printed on its card. Deduplicated first, because a card usually has several drops
    // from the same record.
    private async Task FillDropOrdinals(Dictionary<LootFeedTier, List<LootFeedEntry>> result)
    {
        var needed = new Dictionary<(int Cid, string Src, DateTimeOffset At), int?>();

        foreach (var (_, entries) in result)
        {
            foreach (var e in entries)
            {
                if (e.GameCharacterId is null) continue;
                foreach (var d in e.Drops)
                {
                    // A reported kill count is already authoritative; only fill the gaps.
                    if (d.KillCount is not null || d.OccurredAt is null) continue;
                    needed[(e.GameCharacterId.Value, e.SourceName, d.OccurredAt.Value)] = null;
                }
            }
        }

        if (needed.Count == 0) return;

        var triples = needed.Keys.ToList();
        const string sql = """
            SELECT t.idx,
                   ((SELECT count(*) FROM "LootRecords" r
                     WHERE r."GameCharacterId" = t.cid AND r."SourceName" = t.src AND r."OccurredAt" <= t.at)::int
                    + COALESCE(bl."BaselineKc", 0)) AS ord
            FROM unnest(@cids, @srcs, @ats) WITH ORDINALITY AS t(cid, src, at, idx)
            LEFT JOIN "CharacterSourceBaselines" bl ON bl."GameCharacterId" = t.cid AND bl."SourceName" = t.src
            """;

        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("@cids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer) { Value = triples.Select(t => t.Cid).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("@srcs", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { Value = triples.Select(t => t.Src).ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("@ats", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = triples.Select(t => t.At).ToArray() });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var idx = (int)reader.GetInt64(0) - 1;
                needed[triples[idx]] = reader.GetInt32(1);
            }
        }

        // Stamp the resolved numbers back onto the drops.
        foreach (var (tier, entries) in result)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.GameCharacterId is null) continue;
                if (!e.Drops.Any(d => d.KillCount is null && d.OccurredAt is not null)) continue;

                result[tier][i] = e with
                {
                    Drops = e.Drops.Select(d =>
                    {
                        if (d.KillCount is not null || d.OccurredAt is null) return d;
                        return needed.TryGetValue((e.GameCharacterId.Value, e.SourceName, d.OccurredAt.Value), out var ord) && ord is not null
                            ? d with { KillOrdinal = ord }
                            : d;
                    }).ToList()
                };
            }
        }
    }

    private async Task FillSurvivorOrdinals(
        Dictionary<LootFeedTier, List<LootFeedEntry>> result,
        Dictionary<(LootFeedTier Tier, int Index), DateTimeOffset>? expandedEnds = null)
    {
        // (tier, index) of each entry needing an ordinal, parallel to the unnest input arrays.
        var slots = new List<(LootFeedTier Tier, int Index)>();
        var cids = new List<int>();
        var srcs = new List<string>();
        var firsts = new List<DateTimeOffset>();
        var lasts = new List<DateTimeOffset>();

        foreach (var (tier, entries) in result)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                // Only entries without a RuneLite KillCount fall back to the ordinal label.
                if (e.MinKillCount is not null || e.MaxKillCount is not null || e.GameCharacterId is null)
                    continue;
                slots.Add((tier, i));
                cids.Add(e.GameCharacterId.Value);
                srcs.Add(e.SourceName);
                firsts.Add(e.GroupAnchorAt);
                // Count through the whole session (per ExpandToSessionBounds), not just to the
                // card's last qualifying drop.
                lasts.Add(expandedEnds is not null && expandedEnds.TryGetValue((tier, i), out var end)
                    ? end
                    : e.OccurredAt);
            }
        }

        if (slots.Count == 0) return;

        // before_first = kills strictly before the group's earliest (+1 = that kill's ordinal);
        // at_last = kills at-or-before the group's latest (= the latest kill's ordinal). Counts are
        // by OccurredAt only (no Id tiebreak the old per-row subquery had) — at worst off-by-one on
        // same-timestamp kills, on a fallback-only label. Rides IX_LootRecords_GameCharacterId_SourceName.
        const string sql = """
            SELECT t.idx,
                   ((SELECT count(*) FROM "LootRecords" r
                     WHERE r."GameCharacterId" = t.cid AND r."SourceName" = t.src AND r."OccurredAt" <  t.first)::int + COALESCE(bl."BaselineKc", 0)) AS before_first,
                   ((SELECT count(*) FROM "LootRecords" r
                     WHERE r."GameCharacterId" = t.cid AND r."SourceName" = t.src AND r."OccurredAt" <= t.last)::int + COALESCE(bl."BaselineKc", 0)) AS at_last
            FROM unnest(@cids, @srcs, @firsts, @lasts) WITH ORDINALITY AS t(cid, src, first, last, idx)
            LEFT JOIN "CharacterSourceBaselines" bl ON bl."GameCharacterId" = t.cid AND bl."SourceName" = t.src
            """;

        var connection = dataContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter("@cids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer) { Value = cids.ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter("@srcs", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text) { Value = srcs.ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter("@firsts", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = firsts.ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter("@lasts", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = lasts.ToArray() });

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // idx is the 1-based unnest ordinality, lining up with the order slots were collected.
            var idx = (int)reader.GetInt64(0) - 1;
            var minOrd = reader.GetInt32(1) + 1;
            var maxOrd = reader.GetInt32(2);
            var (tier, entryIndex) = slots[idx];
            result[tier][entryIndex] = result[tier][entryIndex] with
            {
                MinKillOrdinal = minOrd,
                MaxKillOrdinal = maxOrd
            };
        }
    }

    private static List<LootFeedEntry> CollapseProjections(
        List<FeedTierProjection> candidates,
        LootFeedTier tier,
        long tierMin,
        long? tierMax,
        int targetGroups,
        ICollectionLogCache collectionLogCache,
        IItemValueOverrideCache itemValues,
        LootFeedScope scope)
    {
        var groups = new List<LootFeedEntry>();
        // GroupKey -> indices into `groups`. Lets us match records to any same-key group within
        // the feed window (LootFeedGrouping.MaxGap), not just the previous one — needed for
        // interleaved sources (e.g. Shades of Mort'ton gold keys of different colours).
        var indexByKey = new Dictionary<string, List<int>>();

        foreach (var r in candidates)
        {
            // DropsJson holds the raw RuneLite price; re-price through the admin overrides so a
            // rebuilt card classifies into the same swimlane the live publish put it in.
            var allDrops = itemValues.WithEffectivePrices(
                JsonSerializer.Deserialize<List<LootDrop>>(r.DropsJson) ?? []);
            var tierDrops = allDrops
                .Where(d =>
                {
                    // Admin-injected specials have no value; they belong only to the top lane.
                    if (d.IsSpecial) return tier == LootFeedTier.Legendary;
                    var val = (long)d.Quantity * d.Price;
                    return val >= tierMin && (tierMax is null || val < tierMax.Value);
                })
                // KillCount is this record's own, so a drop keeps the KC it landed on when adjacent
                // kills collapse into one card — the card's own Max would climb with the session.
                .Select(d => new LootFeedDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime, collectionLogCache.IsCollectionLogItem(d.ItemId, d.Name), d.IsSpecial, KillCount: r.KillCount, OccurredAt: r.OccurredAt))
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
                MaxKillOrdinal: r.KillOrdinal,
                Scope: scope,
                RunDepth: r.EffectiveKills);

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

    public async Task<CharacterDayFeed> GetCharacterDayFeed(int characterId, DateOnly day)
    {
        try
        {
            // Day boundaries in Europe/London (matching GetActivityCalendar's bucketing),
            // converted to UTC for the OccurredAt range filter.
            var start = IngestTimezone.FromLocalNaive(day.ToDateTime(TimeOnly.MinValue));
            var end = IngestTimezone.FromLocalNaive(day.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var baseQuery = dataContext.LootRecords
                .Where(r => r.GameCharacterId == characterId
                            && r.OccurredAt >= start
                            && r.OccurredAt < end);

            // True totals for the day — every kill, not just the valued ones shown as cards.
            var summary = await baseQuery
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Kills = g.Count(),
                    Gp = g.Sum(r => r.TotalValue),
                    Sources = g.Select(r => r.SourceName).Distinct().Count()
                })
                .FirstOrDefaultAsync();

            // Same join + projection shape as GetAllFeedTiers (LootRecord has no nav properties).
            var candidates = await baseQuery
                .Join(dataContext.GameCharacters, r => r.GameCharacterId, gc => gc.Id, (r, gc) => new { Record = r, Character = gc })
                .Join(dataContext.Users, x => x.Character.UserId, u => u.Id, (x, u) => new { x.Record, x.Character, User = u })
                .OrderByDescending(x => x.Record.OccurredAt)
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
                    EffectiveKills = x.Record.EffectiveKills,
                    KillOrdinal = dataContext.LootRecords.Count(o =>
                        o.GameCharacterId == x.Character.Id
                        && o.SourceName == x.Record.SourceName
                        && (o.OccurredAt < x.Record.OccurredAt
                            || (o.OccurredAt == x.Record.OccurredAt && o.Id <= x.Record.Id)))
                })
                .ToListAsync();

            var entries = CollapseDay(candidates, collectionLogCache, itemValues);

            return new CharacterDayFeed(
                day,
                summary?.Kills ?? 0,
                summary?.Gp ?? 0,
                summary?.Sources ?? 0,
                entries);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get day feed for character {CharacterId} on {Day}", characterId, day);
            throw new RepositoryException("Failed to get day feed", ex);
        }
    }

    // Like CollapseProjections, but keeps every valued drop (>=10K) across all tiers rather than
    // splitting per tier, and has no group cap — a whole day's valued kills, merged into runs.
    private static List<LootFeedEntry> CollapseDay(
        List<FeedTierProjection> candidates,
        ICollectionLogCache collectionLogCache,
        IItemValueOverrideCache itemValues)
    {
        var groups = new List<LootFeedEntry>();
        var indexByKey = new Dictionary<string, List<int>>();

        foreach (var r in candidates)
        {
            // DropsJson holds the raw RuneLite price; re-price through the admin overrides so a
            // rebuilt card classifies into the same swimlane the live publish put it in.
            var allDrops = itemValues.WithEffectivePrices(
                JsonSerializer.Deserialize<List<LootDrop>>(r.DropsJson) ?? []);
            var drops = allDrops
                .Where(d => ILootFeedService.GetDropTier((long)d.Quantity * d.Price) is not null)
                .Select(d => new LootFeedDrop(d.Name, d.Quantity, d.Price, d.IsFirstTime, collectionLogCache.IsCollectionLogItem(d.ItemId, d.Name), d.IsSpecial, KillCount: r.KillCount, OccurredAt: r.OccurredAt))
                .ToList();

            if (drops.Count == 0) continue;

            var total = drops.Sum(d => (long)d.Quantity * d.Price);
            var entry = new LootFeedEntry(
                r.UserName,
                r.UserId,
                r.SourceName,
                r.SourceType,
                total,
                drops,
                r.OccurredAt,
                ILootFeedService.GetDropTier(total) ?? LootFeedTier.Standard,
                r.CharacterName,
                r.GameCharacterId,
                MinKillCount: r.KillCount,
                MaxKillCount: r.KillCount,
                MinKillOrdinal: r.KillOrdinal,
                MaxKillOrdinal: r.KillOrdinal,
                RunDepth: r.EffectiveKills);

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
        }

        // Re-classify each (possibly merged) run by its final combined value so column bucketing
        // reflects the card's real total, not just its first kill.
        return groups
            .Select(g => g with { Tier = ILootFeedService.GetDropTier(g.TotalValue) ?? LootFeedTier.Standard })
            .ToList();
    }

    public async Task<FirstTimeFeed> GetFirstTimeFeed(int characterId, DateTimeOffset? before, int pageSize)
    {
        try
        {
            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // Pull one extra so we know whether more exist beyond this page.
            var take = pageSize + 1;
            // KillOrdinal = chronological position of this kill within (character, source).
            // Same correlated-count pattern as GetSourceDetail, used as fallback when
            // RuneLite didn't report an in-game KillCount.
            const string sql = """
                SELECT lr."OccurredAt",
                       lr."SourceName",
                       lr."SourceType"::text,
                       ld."Name" AS item_name,
                       ld."Quantity" AS qty,
                       (ld."Quantity"::bigint * ld."Price"::bigint) AS value,
                       lr."KillCount",
                       (SELECT COUNT(*)::int FROM "LootRecords" o
                        WHERE o."GameCharacterId" = lr."GameCharacterId"
                          AND o."SourceName" = lr."SourceName"
                          AND (o."OccurredAt" < lr."OccurredAt"
                               OR (o."OccurredAt" = lr."OccurredAt" AND o."Id" <= lr."Id"))) AS kill_ordinal
                FROM "LootRecords" lr
                JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                WHERE lr."GameCharacterId" = @cid
                  AND ld."IsFirstTime" = true
                  AND EXISTS (
                      SELECT 1 FROM "EffectiveCollectionLogItems" cli
                      WHERE cli."ItemId" = ld."ItemId"
                  )
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
                    reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7)));
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

    // Cap so the popover can't turn into an unbounded render for a very busy 48 hours.
    private const int MaxSourcesPerCharacter = 12;
    private const int MaxCharacters = 30;

    public async Task<RecentSessionsPanel> GetRecentSessions(int windowHours, LootFeedScope scope)
    {
        try
        {
            var isLeagues = scope == LootFeedScope.Leagues;
            var from = DateTimeOffset.UtcNow.AddHours(-windowHours);

            var connection = dataContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

            // One row per (character, source) for the whole window - deliberately NOT split into play
            // sessions. The 16h gap rule could break one grind into two or three rows over two days
            // depending on when the player slept, which made the same activity look like several
            // unrelated entries. The question this panel answers is "what has this character been
            // doing", and that is one row per thing however the sittings fell.
            //
            // Dropping sessions also drops the gap-and-islands CTEs and the extra 16h of scan-back
            // they needed, so this is now a plain windowed aggregate.
            const string sql = """
                SELECT lr."GameCharacterId",
                       lr."SourceName",
                       MIN(lr."SourceType"::text) AS source_type,
                       MIN(lr."OccurredAt") AS started,
                       MAX(lr."OccurredAt") AS ended,
                       COUNT(*)::int AS rolls,
                       SUM(lr."TotalValue")::bigint AS gp
                FROM "LootRecords" lr
                JOIN "GameCharacters" gc ON gc."Id" = lr."GameCharacterId"
                WHERE lr."OccurredAt" >= @from
                  AND gc."IsVisible" AND NOT gc."IsAdminHidden" AND gc."IsLeagues" = @isLeagues
                GROUP BY lr."GameCharacterId", lr."SourceName"
                """;

            var rows = new List<RecentSourceRow>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Parameters.Add(new NpgsqlParameter("@from", from));
                cmd.Parameters.Add(new NpgsqlParameter("@isLeagues", isLeagues));

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add(new RecentSourceRow(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        Enum.TryParse<LootSourceType>(reader.GetString(2), ignoreCase: true, out var st) ? st : LootSourceType.Unknown,
                        reader.GetFieldValue<DateTimeOffset>(3),
                        reader.GetFieldValue<DateTimeOffset>(4),
                        reader.GetInt32(5),
                        reader.GetInt64(6)));
                }
            }

            // Filter before the drops query, so the per-item pass only covers rows that will render.
            var filteredOut = rows.Count(r => !LootFeedGrouping.IsNotableActivity(r.Rolls, r.Gp));
            var kept = rows.Where(r => LootFeedGrouping.IsNotableActivity(r.Rolls, r.Gp)).ToList();
            if (kept.Count == 0) return new RecentSessionsPanel(windowHours, [], filteredOut);

            // Per-(character, source) drop facts: the biggest single drop, for the row's tier colour,
            // and how many first-time receipts were collection-log items. Grouped by item name so the
            // row count is bounded by distinct items rather than by rolls.
            var keys = kept.Select(r => SourceKey(r.CharacterId, r.SourceName)).ToArray();
            const string factsSql = """
                SELECT lr."GameCharacterId"::text || @sep || lr."SourceName" AS skey,
                       ld."Name" AS name,
                       MAX(ld."ItemId") AS item_id,
                       MAX(ld."Quantity"::bigint * ld."Price"::bigint) AS best_val,
                       bool_or(ld."IsFirstTime") AS first_time
                FROM "LootRecords" lr
                JOIN "LootDrops" ld ON ld."LootRecordId" = lr."Id"
                WHERE lr."OccurredAt" >= @from
                  AND (lr."GameCharacterId"::text || @sep || lr."SourceName") = ANY(@keys)
                GROUP BY skey, ld."Name"
                """;

            var factsByKey = new Dictionary<string, SourceFacts>(StringComparer.Ordinal);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = factsSql;
                cmd.Parameters.Add(new NpgsqlParameter("@from", from));
                cmd.Parameters.Add(new NpgsqlParameter("@sep", SourceKeySep.ToString()));
                cmd.Parameters.Add(new NpgsqlParameter("@keys", keys));

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var key = reader.GetString(0);
                    var name = reader.GetString(1);
                    var itemId = reader.GetInt32(2);
                    var bestVal = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                    var firstTime = reader.GetBoolean(4);

                    factsByKey.TryGetValue(key, out var facts);
                    facts ??= new SourceFacts();
                    if (bestVal > facts.BestValue) facts.BestValue = bestVal;
                    if (firstTime && collectionLogCache.IsCollectionLogItem(itemId, name)) facts.ClogCount++;
                    factsByKey[key] = facts;
                }
            }

            var characterIds = kept.Select(r => r.CharacterId).Distinct().ToList();
            var names = await dataContext.GameCharacters
                .Where(c => characterIds.Contains(c.Id))
                .Select(c => new { c.Id, Name = c.DisplayName ?? c.User!.FirstName + " " + c.User.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name);

            // Ordered by rolls throughout - busiest character first, and within a character the
            // source they ground hardest. Recency is still on every row as its own timestamp.
            var characters = kept
                .GroupBy(r => r.CharacterId)
                .Select(g => new RecentSessionCharacter(
                    g.Key,
                    names.TryGetValue(g.Key, out var n) ? n : "Unknown",
                    g.Sum(r => r.Rolls),
                    g.Sum(r => r.Gp),
                    g.Max(r => r.Ended),
                    g.OrderByDescending(r => r.Rolls)
                        .ThenByDescending(r => r.Gp)
                        .Take(MaxSourcesPerCharacter)
                        .Select(r =>
                        {
                            var facts = factsByKey.TryGetValue(SourceKey(r.CharacterId, r.SourceName), out var f)
                                ? f
                                : new SourceFacts();
                            return new RecentSession(
                                r.SourceName,
                                r.SourceType,
                                r.Started,
                                r.Ended,
                                r.Rolls,
                                r.Gp,
                                facts.ClogCount,
                                ILootFeedService.GetDropTier(facts.BestValue));
                        })
                        .ToList()))
                .OrderByDescending(c => c.TotalRolls)
                .ThenByDescending(c => c.TotalGp)
                .Take(MaxCharacters)
                .ToList();

            return new RecentSessionsPanel(windowHours, characters, filteredOut);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get recent activity for scope {Scope}", scope);
            throw new RepositoryException("Failed to get recent activity", ex);
        }
    }

    // Composite (character, source) identity as one text value, so the drops query can filter with a
    // single ANY() instead of a two-column IN list.
    private const char SourceKeySep = '\u0001';

    private static string SourceKey(int characterId, string sourceName) =>
        $"{characterId}{SourceKeySep}{sourceName}";

    private sealed record RecentSourceRow(
        int CharacterId,
        string SourceName,
        LootSourceType SourceType,
        DateTimeOffset Started,
        DateTimeOffset Ended,
        int Rolls,
        long Gp);

    // BestValue is kept only to classify the row's feed tier for its edge colour; the item's name
    // isn't carried, because the panel doesn't name it.
    private sealed class SourceFacts
    {
        public long BestValue { get; set; }
        public int ClogCount { get; set; }
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
        // Derived per-run depth for depth-modelled sources (Doom); null otherwise.
        public int? EffectiveKills { get; init; }
    }
}
