using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Log;

public sealed class LootCharacterProfileHandler(
    ILootProfileRepository profileRepository,
    ILootSessionRepository sessionRepository,
    ILootSourceDetailRepository sourceDetailRepository,
    ILootFeedRepository feedRepository,
    CharacterAccessChecker accessChecker,
    SourceLootService sourceLoot,
    ICharacterDelveDepthRepository delveDepths,
    IMemoryCache cache)
{
    public async Task<Result<ProfileHeader>> HandleHeader(int characterId)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<ProfileHeader>.Failure("Character not found.");

        var header = await profileRepository.GetProfileHeader(characterId);
        return header is null
            ? Result<ProfileHeader>.Failure("Character not found.")
            : Result<ProfileHeader>.Success(header);
    }

    public const int SessionsPageSize = 20;

    public async Task<Result<CharacterSessionHistory>> HandleCharacterSessions(int characterId, int pageNumber = 1)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<CharacterSessionHistory>.Failure("Character not found.");

        // Fetch cumulatively (first pageNumber*SessionsPageSize sessions) so "load more" re-renders
        // the whole day-grouped list — day headers then stay correct across the page boundary. The
        // gap-islands CTE scans all the character's rows regardless, so the only added cost is
        // rendering the already-loaded cards again.
        var result = await sessionRepository.GetCharacterSessions(characterId, 1, pageNumber * SessionsPageSize);
        return Result<CharacterSessionHistory>.Success(result);
    }

    public async Task<Result<ProfileWindowStats>> HandleWindowStats(int characterId)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<ProfileWindowStats>.Failure("Character not found.");

        // Sequential awaits — the scoped DbContext can only handle one query at a time.
        var now = DateTimeOffset.UtcNow;
        var last7d = await profileRepository.GetWindowStats(characterId, now.AddDays(-7), null);
        var last30d = await profileRepository.GetWindowStats(characterId, now.AddDays(-30), null);
        var allTime = await profileRepository.GetWindowStats(characterId, null, null);

        return Result<ProfileWindowStats>.Success(new ProfileWindowStats(last7d, last30d, allTime));
    }

    public async Task<Result<MonthlyTrend>> HandleMonthlyTrend(int characterId, string range)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<MonthlyTrend>.Failure("Character not found.");

        var version = LootStatsCache.GetVersion(cache, characterId);
        var key = LootStatsCache.EntryKey(characterId, version, nameof(HandleMonthlyTrend), range);
        var trend = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = LootStatsCache.EntryTtl;
            var now = DateTimeOffset.UtcNow;
            var to = now.AddDays(1);
            // "12m" = last 12 *calendar* months (inclusive of current). Anchor on the first
            // of the month 11 back so the bar count is stable across the month transition.
            DateTimeOffset? from = range == "all"
                ? null
                : new DateTimeOffset(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)).AddMonths(-11);
            return await profileRepository.GetMonthlyTrend(characterId, from, to, range);
        });
        return Result<MonthlyTrend>.Success(trend!);
    }

    public async Task<Result<MonthlyRollTrend>> HandleMonthlyRolls(int characterId, string range)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<MonthlyRollTrend>.Failure("Character not found.");

        var version = LootStatsCache.GetVersion(cache, characterId);
        var key = LootStatsCache.EntryKey(characterId, version, nameof(HandleMonthlyRolls), range);
        var trend = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = LootStatsCache.EntryTtl;
            // Same window arithmetic as HandleMonthlyTrend, deliberately duplicated rather than
            // shared: the two charts sit one above the other and must always cover exactly the
            // same months, so their bounds are defined identically at the same layer.
            var now = DateTimeOffset.UtcNow;
            var to = now.AddDays(1);
            DateTimeOffset? from = range == "all"
                ? null
                : new DateTimeOffset(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)).AddMonths(-11);
            return await profileRepository.GetMonthlyRolls(characterId, from, to, range);
        });
        return Result<MonthlyRollTrend>.Success(await CountDepthSourcesInDelves(characterId, trend!));
    }

    /// <summary>
    /// Restates depth-modelled sources in this chart from claims into delves.
    ///
    /// The chart answers "how much grinding happened, and at what", and a claim is the wrong unit for
    /// a source where one claim covers a whole descent: Doom logs one record per run however many
    /// levels it cleared, so a heavy month showed as a few dozen rolls and the biggest grind on the
    /// page rendered as one of the smallest slices. A delve is the thing the player actually did
    /// repeatedly, and it is the unit Doom's rates are quoted in.
    ///
    /// Deliberately OUTSIDE the memory cache above. The multiplier is the admin's per-character
    /// average delve depth, and setting that does not bump the character's loot-stats version — so a
    /// cached scaled trend would keep serving the old depth for up to the cache TTL after an admin
    /// corrected it. The cache holds the raw query; the units are applied per request, which is a
    /// dictionary lookup and one small indexed read per depth-modelled source present.
    /// </summary>
    private async Task<MonthlyRollTrend> CountDepthSourcesInDelves(int characterId, MonthlyRollTrend trend)
    {
        var depthSources = trend.Months
            .SelectMany(m => m.TopSources)
            .Select(s => s.SourceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(sourceLoot.HasDepthModel)
            .ToList();
        if (depthSources.Count == 0) return trend;

        var depthBySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceName in depthSources)
        {
            // Sequential awaits — one scoped DbContext, one query at a time.
            var overrideDepth = await delveDepths.GetAverageDepth(characterId, sourceName);
            var depth = sourceLoot.AverageDepthPerRun(sourceName, overrideDepth);
            if (depth is > 1) depthBySource[sourceName] = depth.Value;
        }
        if (depthBySource.Count == 0) return trend;

        var months = new List<RollMonthBucket>(trend.Months.Count);
        foreach (var m in trend.Months)
        {
            var added = 0;
            var sources = new List<RollSourceSegment>(m.TopSources.Count);
            foreach (var s in m.TopSources)
            {
                if (depthBySource.TryGetValue(s.SourceName, out var depth))
                {
                    var delves = s.Rolls * depth;
                    added += delves - s.Rolls;
                    sources.Add(s with { Rolls = delves, DelvesPerRun = depth });
                }
                else
                {
                    sources.Add(s);
                }
            }

            // The month total takes the same delta, so the bar still matches the stack standing in it
            // and the chart's "Other" remainder — total minus what was named — comes out unchanged.
            //
            // A depth-modelled source that fell outside the query's per-month source cap is not here
            // to be restated, so its claims stay counted as claims inside Other. That only happens
            // when it was one of a month's smallest contributors anyway, which is the case where the
            // difference is invisible.
            months.Add(m with { Rolls = m.Rolls + added, TopSources = sources });
        }

        return trend with { Months = months };
    }

    public async Task<Result<HeatmapData>> HandleHeatmap(int characterId, HeatmapMode mode)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<HeatmapData>.Failure("Character not found.");

        var version = LootStatsCache.GetVersion(cache, characterId);
        var key = LootStatsCache.EntryKey(characterId, version, nameof(HandleHeatmap), mode.ToString());
        var data = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = LootStatsCache.EntryTtl;
            var now = DateTimeOffset.UtcNow;
            var from = now.AddDays(-364); // 365 cells inclusive
            var days = await profileRepository.GetActivityCalendar(characterId, from, now.AddDays(1));
            return new HeatmapData(
                DateOnly.FromDateTime(from.UtcDateTime),
                DateOnly.FromDateTime(now.UtcDateTime),
                mode,
                days);
        });
        return Result<HeatmapData>.Success(data!);
    }

    public async Task<Result<PersonalRecords>> HandleRecords(int characterId)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<PersonalRecords>.Failure("Character not found.");

        var version = LootStatsCache.GetVersion(cache, characterId);
        var key = LootStatsCache.EntryKey(characterId, version, nameof(HandleRecords));
        var records = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = LootStatsCache.EntryTtl;
            return await profileRepository.GetPersonalRecords(characterId);
        });
        return Result<PersonalRecords>.Success(records!);
    }

    public async Task<Result<SourceCollection>> HandleSourceCollection(int characterId, string sourceName)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<SourceCollection>.Failure("Character not found.");

        var collection = await sourceDetailRepository.GetSourceCollection(characterId, sourceName);

        // One shared normalisation for every consumer of Runs: empty for sources with no depth
        // model, the admin's average applied when one is configured, and otherwise the assumed
        // default, so the model never waits on the derivation backfill.
        var overrideDepth = await delveDepths.GetAverageDepth(characterId, sourceName);
        collection = collection with { Runs = sourceLoot.NormaliseRuns(sourceName, collection.Runs, overrideDepth) };

        // Normalise every item's expected kills-to-drop through SourceLootService so the character
        // page's rate column, luck pills and distribution charts agree with the leaderboard and
        // the live feed: raid unique-table shares, multi-roll tables, Doom's per-run depth model,
        // and admin rate modifiers all land here. EffectiveRarity is what the Rate column renders,
        // so an overridden or depth-derived rate is visible rather than silently applied.
        var allDepths = Depths(collection.Runs);

        var enriched = collection with
        {
            Entries = collection.Entries
                .Select(e =>
                {
                    // The FULL depth profile, deliberately, not a window up to the first drop.
                    // This page states luck as "where you are now" (SourceCollectionPanel compares
                    // against the character's current total), so the expectation must cover the same
                    // span. Windowing one side only made obtained items read as dry: an expectation
                    // built from the first few runs, judged against all the progress since. The
                    // leaderboard is the place that windows, because there the observed value is the
                    // drop's own kill count.
                    var rate = sourceLoot.EffectiveRate(sourceName, e.ItemName, e.RarityNumerator, e.RarityDenominator, e.Rolls, allDepths);
                    // A source that owns its rates (Doom) has no usable figure for items its model
                    // doesn't cover — its guaranteed accumulating drops carry a stored per-level
                    // rarity that means nothing per run. Blank the wiki values too, so no consumer
                    // falls back to them and prints "7/104, very dry" for an item you get every run.
                    if (rate is null && Owns(sourceName))
                        return e with { EffectiveKcPerDrop = null, EffectiveRarity = null, Rarity = null, RarityNumerator = null, RarityDenominator = null };
                    return e with { EffectiveKcPerDrop = rate?.ExpectedKc, EffectiveRarity = rate?.Rarity };
                })
                .ToList(),
            MissingItems = collection.MissingItems
                .Select(m =>
                {
                    // Still-missing items are measured against every run done so far.
                    var rate = sourceLoot.EffectiveRate(sourceName, m.ItemName, m.RarityNumerator, m.RarityDenominator, m.Rolls, allDepths);
                    if (rate is null && Owns(sourceName))
                        return m with { EffectiveKcPerDrop = null, EffectiveRarity = null, Rarity = null, RarityNumerator = null, RarityDenominator = null };
                    return m with { EffectiveKcPerDrop = rate?.ExpectedKc, EffectiveRarity = rate?.Rarity };
                })
                .ToList()
        };
        return Result<SourceCollection>.Success(enriched);
    }

    // True when the source's strategy is the sole authority on its rates, so an unmodelled item
    // must show no rate at all rather than the misleading stored one.
    private bool Owns(string sourceName) => sourceLoot.OverridesStoredRates(sourceName);

    private static List<int> Depths(IReadOnlyList<SourceRun> runs) =>
        runs.Select(r => r.Depth).ToList();


    public async Task<Result<SourceKillTrend>> HandleSourceKillTrend(int characterId, string sourceName)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<SourceKillTrend>.Failure("Character not found.");

        var version = LootStatsCache.GetVersion(cache, characterId);
        var key = LootStatsCache.EntryKey(characterId, version, nameof(HandleSourceKillTrend), sourceName);
        var trend = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = LootStatsCache.EntryTtl;
            return await sourceDetailRepository.GetSourceKillTrend(characterId, sourceName);
        });
        return Result<SourceKillTrend>.Success(trend!);
    }

    public async Task<Result<FirstTimeFeed>> HandleFirstTimeFeed(int characterId, DateTimeOffset? before, int pageSize)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<FirstTimeFeed>.Success(new FirstTimeFeed([], null, false));

        var version = LootStatsCache.GetVersion(cache, characterId);
        var args = $"{before?.UtcTicks.ToString() ?? "null"}:{pageSize}";
        var key = LootStatsCache.EntryKey(characterId, version, nameof(HandleFirstTimeFeed), args);
        var feed = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = LootStatsCache.EntryTtl;
            return await feedRepository.GetFirstTimeFeed(characterId, before, pageSize);
        });
        return Result<FirstTimeFeed>.Success(feed!);
    }

    public async Task<Result<TopItemsList>> HandleTopItems(int characterId, int limit)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<TopItemsList>.Failure("Character not found.");

        var version = LootStatsCache.GetVersion(cache, characterId);
        var key = LootStatsCache.EntryKey(characterId, version, nameof(HandleTopItems), limit.ToString());
        var data = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.SlidingExpiration = LootStatsCache.EntryTtl;
            return await profileRepository.GetTopItems(characterId, limit);
        });
        return Result<TopItemsList>.Success(data!);
    }

    public async Task<Result<CharacterDayFeed>> HandleDayFeed(int characterId, DateOnly day)
    {
        if (!await accessChecker.CanAccess(characterId))
            return Result<CharacterDayFeed>.Failure("Character not found.");

        var feed = await feedRepository.GetCharacterDayFeed(characterId, day);
        return Result<CharacterDayFeed>.Success(feed);
    }

}
