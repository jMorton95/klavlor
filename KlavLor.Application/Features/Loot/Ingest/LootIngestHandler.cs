using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using KlavLor.Application.Common;
using KlavLor.Application.Features.Drop;
using KlavLor.Application.Features.Loot.Feed;
using KlavLor.Application.Features.Loot.Log;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Features.Source;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Ingest;

public sealed class LootIngestHandler(
    ILootRecordRepository lootRecordRepository,
    IGameCharacterRepository gameCharacterRepository,
    LootIngestValidator validator,
    ICurrentUser currentUser,
    ILootFeedService lootFeedService,
    ILootRollFeed lootRollFeed,
    IUserRepository userRepository,
    ICollectionLogCache collectionLogCache,
    IMemoryCache memoryCache,
    IDropRateRepository dropRateRepository,
    ICharacterDelveDepthRepository delveDepthRepository,
    IItemValueOverrideCache itemValues,
    SourceLootService sourceLoot)
{
    /// <summary>
    /// The most rolls one ingest batch may put on the live ticker.
    /// </summary>
    /// <remarks>
    /// Below the ticker buffer's capacity (40) on purpose: anything beyond that is evicted before a
    /// viewer could read it, so publishing it only costs SSE frames and DOM swaps. The cap is what
    /// stops a returning player's backlog flooding every open banner.
    /// </remarks>
    internal const int MaxRollsPublishedPerBatch = 25;

    private static readonly string[] DateFormats =
    [
        "MMM dd, yyyy, h:mm:ss tt",
        "MMM d, yyyy, h:mm:ss tt",
        "MMM dd, yyyy, h:mm:ss a",
        "MMM d, yyyy, h:mm:ss a"
    ];

    public async Task<Result> Handle(LootIngestCommand command)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return Result.Failure("Validation failed.");

        var userId = currentUser.UserId;
        if (userId is null)
            return Result.Failure("User not authenticated.");

        var character = await ResolveCharacter(userId.Value, command.CharacterId);

        var parsed = MapToLootRecord(command, userId.Value, character?.Id);
        if (parsed is null)
            return Result.Failure("Failed to parse loot data.");

        ApplyEffectiveKills(parsed);
        var (record, drops) = parsed;

        if (record.ContentHash is not null)
        {
            var existing = await lootRecordRepository.FindExistingHashes(userId.Value, [record.ContentHash]);
            if (existing.Count > 0)
                return Result.Success(); // duplicate, skip
        }

        if (character is not null)
        {
            var seen = await lootRecordRepository.GetSeenItemNames(character.Id, record.OccurredAt);
            drops = drops.Select(d => d with { IsFirstTime = !seen.Contains(d.Name) }).ToList();
        }
        FinalizeDrops(record, drops);

        await lootRecordRepository.SaveLootRecord(record);

        // Imported records can land earlier than existing ones for this character —
        // re-sweep so any pre-existing later records lose their stale IsFirstTime flag.
        if (record.IsImported && character is not null)
            await lootRecordRepository.RecomputeFirstTimeFlags(character.Id);

        if (character is not null)
        {
            LootStatsCache.Invalidate(memoryCache, character.Id);
            // Source pages aggregate over all characters — bump only this source's version.
            GlobalSourceCache.Invalidate(memoryCache, record.SourceName);
            // Drop pages aggregate per item — bump each dropped item's version.
            foreach (var drop in drops)
                GlobalDropCache.Invalidate(memoryCache, drop.Name);
        }

        // NOTE: ingest never touches template-node completion. Progression is manual-only —
        // the drop-driven auto-completion (and its generated notes) was removed deliberately.

        // Every visible live kill reaches the roll ticker, whatever it dropped — a dry Vorkath is
        // exactly what the swimlanes never show. The ordinal is resolved once here and handed to
        // both the ticker and the feed card, so the two cannot disagree about the same kill.
        var ordinals = await ResolveOrdinals([(record, character)]);
        PublishRoll(record, character, ordinals);

        if (ShouldPublishToFeed(record, character))
            await PublishToFeed(userId.Value, record, character, ordinals);
        return Result.Success();
    }

    public async Task<Result> HandleBatch(List<LootIngestCommand> commands)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Result.Failure("User not authenticated.");

        // Resolve characters for all unique characterIds in the batch.
        var characterCache = new Dictionary<string, GameCharacter?>();
        foreach (var charId in commands.Select(c => c.CharacterId).Where(c => !string.IsNullOrEmpty(c)).Distinct())
        {
            characterCache[charId!] = await ResolveCharacter(userId.Value, charId);
        }

        var parsedItems = new List<(ParsedRecord Parsed, GameCharacter? Character)>();
        foreach (var command in commands)
        {
            var validationResult = await validator.ValidateAsync(command);
            if (!validationResult.IsValid)
                continue;

            var character = command.CharacterId is not null && characterCache.TryGetValue(command.CharacterId, out var cached)
                ? cached : null;

            var parsed = MapToLootRecord(command, userId.Value, character?.Id);
            if (parsed is not null)
            {
                ApplyEffectiveKills(parsed);
                parsedItems.Add((parsed, character));
            }
        }

        if (parsedItems.Count == 0)
            return Result.Failure("No valid records to import.");

        // Deduplicate: find which content hashes already exist in the database.
        var hashes = parsedItems
            .Where(r => r.Parsed.Record.ContentHash is not null)
            .Select(r => r.Parsed.Record.ContentHash!)
            .ToList();

        if (hashes.Count > 0)
        {
            var existing = await lootRecordRepository.FindExistingHashes(userId.Value, hashes);
            if (existing.Count > 0)
            {
                parsedItems = parsedItems
                    .Where(r => r.Parsed.Record.ContentHash is null || !existing.Contains(r.Parsed.Record.ContentHash))
                    .ToList();
                if (parsedItems.Count == 0)
                    return Result.Success(); // all duplicates, nothing to insert
            }
        }

        // Compute IsFirstTime per character. Walking each character's records in
        // OccurredAt order and updating a local seen set keeps in-batch firsts
        // marked only on their earliest receipt. Anything that lands before
        // already-saved records gets fixed by RecomputeFirstTimeFlags below.
        var charactersTouched = new HashSet<int>();
        foreach (var group in parsedItems.GroupBy(p => p.Character?.Id))
        {
            if (group.Key is null) continue;
            var cid = group.Key.Value;
            charactersTouched.Add(cid);

            var orderedByTime = group.OrderBy(p => p.Parsed.Record.OccurredAt).ToList();
            var earliest = orderedByTime[0].Parsed.Record.OccurredAt;
            var seen = await lootRecordRepository.GetSeenItemNames(cid, earliest);

            foreach (var (parsed, _) in orderedByTime)
            {
                var newDrops = new List<LootDrop>(parsed.Drops.Count);
                foreach (var d in parsed.Drops)
                {
                    var first = !seen.Contains(d.Name);
                    newDrops.Add(d with { IsFirstTime = first });
                    if (first) seen.Add(d.Name);
                }
                // Replace Drops list contents in place so the outer reference stays consistent.
                parsed.Drops.Clear();
                parsed.Drops.AddRange(newDrops);
            }
        }

        foreach (var (parsed, _) in parsedItems)
            FinalizeDrops(parsed.Record, parsed.Drops);

        var allRecords = parsedItems.Select(p => p.Parsed.Record).ToList();
        await lootRecordRepository.SaveLootRecords(allRecords);

        // Imports may slot in earlier than existing records — fix per character.
        if (parsedItems.Any(p => p.Parsed.Record.IsImported))
        {
            foreach (var cid in charactersTouched)
                await lootRecordRepository.RecomputeFirstTimeFlags(cid);
        }

        foreach (var cid in charactersTouched)
            LootStatsCache.Invalidate(memoryCache, cid);

        // Source pages aggregate over all visible characters — bump only the versions of
        // the sources this batch actually touched (records tied to a character).
        var sourcesTouched = parsedItems
            .Where(p => p.Character is not null)
            .Select(p => p.Parsed.Record.SourceName)
            .Distinct(StringComparer.Ordinal);
        foreach (var source in sourcesTouched)
            GlobalSourceCache.Invalidate(memoryCache, source);

        // Drop pages aggregate per item — bump each touched item's version (records tied to a
        // character, matching the source/visibility rule above).
        var itemsTouched = parsedItems
            .Where(p => p.Character is not null)
            .SelectMany(p => p.Parsed.Drops.Select(d => d.Name))
            .Distinct(StringComparer.Ordinal);
        foreach (var item in itemsTouched)
            GlobalDropCache.Invalidate(memoryCache, item);

        // NOTE: no template-node completion here either — see the comment in Handle.

        // ONE ordinal resolution for the whole batch, shared by the ticker and the feed. This used
        // to be two queries per published record inside PublishRecordToFeed; at 250 records to a
        // sync batch that was up to 500 round-trips on the ingest hot path, and the ticker needs an
        // ordinal for every kill rather than only the ones valuable enough to publish.
        // ONLY THE NEWEST FEW REACH THE TICKER. klavlor-sync tails, so a player returning after a
        // long break syncs thousands of kills as LIVE, 250 to a batch - and every one of those
        // would be an SSE frame and a DOM swap for every person watching, for a banner that holds
        // 40 and shows about 16. The older ones would be pushed off before anyone could read them,
        // so publishing them is pure waste; capping trims exactly the ones nobody would see.
        //
        // A normal sync is a handful of kills and is unaffected.
        var tickerRecords = TrimToNewestRolls(parsedItems
            .Where(p => IsCharacterVisible(p.Character) && !p.Parsed.Record.IsImported)
            .Select(p => (p.Parsed.Record, p.Character)));

        var liveRecords = parsedItems
            .Where(p => ShouldPublishToFeed(p.Parsed.Record, p.Character))
            .Select(p => (p.Parsed.Record, p.Character))
            .ToList();

        var ordinals = await ResolveOrdinals(tickerRecords.Concat(liveRecords));

        foreach (var (record, character) in tickerRecords)
            PublishRoll(record, character, ordinals);

        if (liveRecords.Count > 0)
        {
            var user = await userRepository.GetById(userId.Value);
            var userName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";
            foreach (var (record, character) in liveRecords)
            {
                await PublishRecordToFeed(userName, record, character, ordinals);
            }
        }

        return Result.Success();
    }

    private async Task<GameCharacter?> ResolveCharacter(int userId, string? characterId)
    {
        if (string.IsNullOrEmpty(characterId))
            return null;

        var existing = await gameCharacterRepository.GetByUserAndRuneLiteId(userId, characterId);
        if (existing is not null)
            return existing;

        var newCharacter = new GameCharacter
        {
            UserId = userId,
            RuneLiteId = characterId,
            DisplayName = Guid.NewGuid().ToString(),
            IsVisible = false
        };

        return await gameCharacterRepository.Save(newCharacter);
    }

    private bool ShouldPublishToFeed(LootRecord record, GameCharacter? character)
    {
        if (!IsCharacterVisible(character))
            return false;

        // Imported records only publish if any single drop qualifies for Rare+ (1M+) to avoid flooding.
        if (record.IsImported)
        {
            // Re-priced through the override cache: DropsJson holds the raw price, so an item whose
            // real worth is admin-set would otherwise be judged at 0 and silently never publish.
            var drops = itemValues.WithEffectivePrices(
                JsonSerializer.Deserialize<List<LootDrop>>(record.DropsJson) ?? []);
            return drops.Any(d => (long)d.Quantity * d.Price >= 1_000_000);
        }

        return true;
    }

    private static bool IsCharacterVisible(GameCharacter? character)
    {
        // Records without a character are always visible (legacy data).
        if (character is null)
            return true;

        return character.IsVisible && !character.IsAdminHidden;
    }

    /// <summary>
    /// The newest rolls of a batch, oldest first, capped at <see cref="MaxRollsPublishedPerBatch"/>.
    /// </summary>
    /// <remarks>
    /// Oldest first because the banner prepends: publishing in chronological order leaves the newest
    /// leftmost, the same way live rolls arrive. Ordered by (OccurredAt, Id) - the tie-break every
    /// other roll-ordering query uses - so two kills sharing a timestamp keep a stable order.
    /// </remarks>
    internal static List<(LootRecord Record, GameCharacter? Character)> TrimToNewestRolls(
        IEnumerable<(LootRecord Record, GameCharacter? Character)> candidates) =>
        candidates
            .OrderBy(x => x.Record.OccurredAt)
            .ThenBy(x => x.Record.Id)
            .TakeLast(MaxRollsPublishedPerBatch)
            .ToList();

    /// <summary>
    /// Kill ordinals for a set of records, in one round-trip, keyed by record id.
    /// </summary>
    /// <remarks>
    /// Only records that actually need one are asked for: a record RuneLite gave a KillCount for
    /// already has its roll number, and one with no character or no id (the single-record path
    /// before the save) has no ordinal to resolve. Distinct by record id because the ticker set and
    /// the feed set overlap - most published records are in both.
    /// </remarks>
    private async Task<Dictionary<int, int>> ResolveOrdinals(
        IEnumerable<(LootRecord Record, GameCharacter? Character)> records)
    {
        var requests = records
            .Where(x => x.Character is not null && x.Record.Id > 0)
            .DistinctBy(x => x.Record.Id)
            .Select(x => new KillOrdinalRequest(
                x.Record.Id, x.Character!.Id, x.Record.SourceName, x.Record.OccurredAt))
            .ToList();

        return requests.Count == 0 ? [] : await lootRecordRepository.GetKillOrdinals(requests);
    }

    /// <summary>
    /// Puts one kill on the live roll ticker. No loot, no value floor, no tier - see LootRollEntry.
    /// </summary>
    /// <remarks>
    /// IMPORTED RECORDS NEVER REACH HERE. A first sync with full history is thousands of kills that
    /// happened months ago, and a banner labelled live must not replay them; the callers filter on
    /// IsImported before calling. That is the same reason the feed damps imports, just absolute
    /// rather than by value.
    /// </remarks>
    private void PublishRoll(LootRecord record, GameCharacter? character, Dictionary<int, int> ordinals)
    {
        if (!IsCharacterVisible(character) || record.IsImported) return;

        // RuneLite's own count wins where it gave one; otherwise our resolved chronological
        // position. Same precedence as the feed card.
        var ordinal = record.KillCount is > 0
            ? record.KillCount
            : ordinals.TryGetValue(record.Id, out var resolved) ? resolved : null;

        var scope = character?.IsLeagues == true ? LootFeedScope.Leagues : LootFeedScope.Main;

        lootRollFeed.Publish(scope, new LootRollEntry(
            character?.GetEffectiveName() ?? "Unknown",
            character?.Id,
            record.SourceName,
            ordinal,
            record.OccurredAt));
    }

    private async Task PublishToFeed(int userId, LootRecord record, GameCharacter? character, Dictionary<int, int> ordinals)
    {
        var user = await userRepository.GetById(userId);
        var userName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";
        await PublishRecordToFeed(userName, record, character, ordinals);
    }

    private async Task PublishRecordToFeed(
        string userName, LootRecord record, GameCharacter? character, Dictionary<int, int> ordinals)
    {
        // Re-priced through the override cache before anything looks at a value: this is the live
        // path, and it must agree with the backfill path (which reads the already-effective
        // LootDrops projection) or the same drop would land in a different swimlane before and
        // after a refresh.
        var drops = itemValues.WithEffectivePrices(
            JsonSerializer.Deserialize<List<LootDrop>>(record.DropsJson) ?? []);

        // Attach the effective rate to every drop so a feed card can say how lucky it was using
        // exactly the same numbers as the character page and the leaderboard. One batched lookup
        // per record. The depth passed in is THIS record's own derived depth, so a depth-modelled
        // source (Doom) is rated against the run that actually produced the drop.
        var rates = await dropRateRepository.GetRates(record.SourceName, drops.Select(d => d.Name).ToList());

        // Depth for a depth-modelled source, resolved through the one shared policy so this card
        // agrees with the character page and the leaderboard — including the admin's per-character
        // average, which this path used to ignore.
        int? overrideDepth = character is not null && sourceLoot.HasDepthModel(record.SourceName)
            ? await delveDepthRepository.GetAverageDepth(character.Id, record.SourceName)
            : null;
        var runDepths = sourceLoot.RunDepthsForClaim(record.SourceName, record.EffectiveKills, overrideDepth);

        // Chronological ordinal — the fallback roll number when RuneLite didn't supply a kill
        // count, which chest-style sources routinely don't. Resolved BEFORE the drops are built so
        // each one can carry it: falling back to the card's first ordinal instead made every drop
        // on such a card claim the session's opening roll. One lookup per record, shared by all
        // tiers.
        // Resolved for the whole batch by ResolveOrdinals, not per record: this was two round-trips
        // each (the count and the admin baseline), and it is the same figure the roll ticker shows,
        // so both read it from here.
        int? ordinal = ordinals.TryGetValue(record.Id, out var resolvedOrdinal) ? resolvedOrdinal : null;

        var feedDrops = drops.Select(d =>
        {
            rates.TryGetValue(d.Name, out var rate);
            var effective = sourceLoot.EffectiveRate(
                record.SourceName, d.Name, rate?.RarityNumerator, rate?.RarityDenominator,
                rate?.Rolls ?? 1, runDepths);
            return new LootFeedDrop(
                d.Name, d.Quantity, d.Price, d.IsFirstTime,
                collectionLogCache.IsCollectionLogItem(d.ItemId, d.Name), d.IsSpecial,
                effective?.ExpectedKc, effective?.Rarity,
                // This record's own numbers, so the drop keeps the roll it landed on even after
                // the card merges another hundred rolls onto the same session.
                KillCount: record.KillCount,
                KillOrdinal: ordinal,
                OccurredAt: record.OccurredAt,
                // A record being published live was created moments ago, so it cannot yet carry an
                // admin luck exclusion. Read from the record anyway rather than hardcoded, so this
                // path and the backfill (LootFeedTiersHandler) state the same thing about the same
                // drop if a republish ever happens.
                ExcludedFromLuck: record.ExcludedFromLuck);
        }).ToList();
        var dropsByTier = ILootFeedService.ClassifyDropsByTier(feedDrops);
        if (dropsByTier.Count == 0) return;

        // Bounds of the play session this kill belongs to, so a card's KC range spans the whole
        // session rather than starting at its first tier-qualifying drop. Site-wide session
        // rules for every tier, so all swimlanes agree with each other and with the character
        // page's session history.
        SessionKcBounds? bounds = null;
        if (character is not null && record.Id > 0)
        {
            bounds = await lootRecordRepository.GetSessionBounds(
                character.Id, record.SourceName, record.OccurredAt,
                LootFeedGrouping.MaxGap, LootFeedGrouping.SessionBreakGap);
        }

        // Route to the matching feed scope. Characters without IsLeagues set default to Main.
        var scope = character?.IsLeagues == true ? LootFeedScope.Leagues : LootFeedScope.Main;

        // No depth conversion on the observed side. A depth-modelled rate is expressed per RUN
        // (DoomLootStrategy.ExpectedCompletionsForRuns returns expected runs, and the character page
        // judges it against the plain run count), so multiplying the observed figure by the delve
        // depth — as this used to — inflated every Doom card's dryness by the whole depth factor,
        // and only on live cards: the backfill path never set it, so the same drop read differently
        // before and after a refresh. The kill-count fallback in LootFeedItem is already the run
        // count, which is the correct basis for both paths.

        foreach (var (tier, tierDrops) in dropsByTier)
        {
            // Skip Standard/Uncommon tiers for imported records to avoid flooding.
            if (record.IsImported && tier is LootFeedTier.Standard or LootFeedTier.Uncommon)
                continue;

            var tierTotal = tierDrops.Sum(d => (long)d.Quantity * d.Price);
            lootFeedService.Publish(new LootFeedEntry(
                userName,
                record.UserId,
                record.SourceName,
                record.SourceType,
                tierTotal,
                tierDrops,
                record.OccurredAt,
                tier,
                character?.GetEffectiveName(userName),
                character?.Id,
                GroupStartedAt: bounds?.StartedAt,
                MinKillCount: bounds?.MinKillCount ?? record.KillCount,
                MaxKillCount: bounds?.MaxKillCount ?? record.KillCount,
                MinKillOrdinal: bounds?.FirstOrdinal ?? ordinal,
                MaxKillOrdinal: ordinal,
                Scope: scope));
        }
    }

    private static ParsedRecord? MapToLootRecord(LootIngestCommand command, int userId, int? gameCharacterId)
    {
        if (!Enum.TryParse<LootSourceType>(command.Type, ignoreCase: true, out var sourceType))
            sourceType = LootSourceType.Unknown;

        DateTimeOffset occurredAt;
        if (DateTime.TryParseExact(
                command.Date, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localDt))
        {
            occurredAt = IngestTimezone.FromLocalNaive(localDt);
        }
        else
        {
            occurredAt = DateTimeOffset.UtcNow;
        }

        var drops = command.Drops.Select(d => new LootDrop(d.Name, d.Id, d.Quantity, d.Price)).ToList();
        // Provisional: the raw total. FinalizeDrops recomputes it from the effective prices once the
        // item value overrides have been applied.
        var totalValue = drops.Sum(d => (long)d.Quantity * d.Price);

        var record = new LootRecord
        {
            UserId = userId,
            SourceName = command.Name,
            SourceType = sourceType,
            CombatLevel = command.Level == -1 ? null : command.Level,
            KillCount = command.KillCount == -1 ? null : command.KillCount,
            TotalValue = totalValue,
            DropsJson = "[]", // finalized after IsFirstTime is applied
            OccurredAt = occurredAt,
            ContentHash = command.ContentHash,
            IsImported = command.Imported,
            GameCharacterId = gameCharacterId
        };

        return new ParsedRecord(record, drops);
    }

    // Compute the per-source derived metric (e.g. Doom's delve depth) through the strategy
    // facade and stash it on the record. Ordinary sources have no special strategy, so this
    // leaves EffectiveKills null (implicitly one roll per kill). Uses the parsed drops, which
    // already carry the names/quantities the strategy needs (independent of IsFirstTime).
    private void ApplyEffectiveKills(ParsedRecord parsed)
    {
        var source = parsed.Record.SourceName;
        if (!sourceLoot.HasSpecialModel(source)) return;

        var claim = parsed.Drops.Select(d => new ClaimDrop(d.Name, d.Quantity)).ToList();
        parsed.Record.EffectiveKills = sourceLoot.EffectiveKills(source, claim);
        parsed.Record.EffectiveKillsVersion = SourceLootService.DerivationVersion;
    }

    private sealed record ParsedRecord(LootRecord Record, List<LootDrop> Drops);

    private void FinalizeDrops(LootRecord record, List<LootDrop> drops)
    {
        // DropsJson stays the canonical record and keeps the RAW price RuneLite reported, exactly
        // as it arrived. The normalised LootDrop rows and TotalValue are the DERIVED projection and
        // carry the EFFECTIVE price — the admin's intrinsic value override applied on top of the raw
        // one. Keeping the raw figure in the JSON is what makes an override reversible: removing it
        // re-derives straight back from here (see ItemValueOverrideRepository.RebuildForItem).
        //
        // Both representations are written from the same list in the same order, so index alignment
        // between them holds; the rebuild pass relies on it.
        record.DropsJson = JsonSerializer.Serialize(drops);

        var rows = drops.Select(d => new LootDropRow
        {
            ItemId = d.ItemId,
            Name = d.Name,
            Quantity = d.Quantity,
            Price = itemValues.GetPrice(d.ItemId, d.Price),
            IsFirstTime = d.IsFirstTime,
            IsSpecial = d.IsSpecial
        }).ToList();

        record.ReplaceDropRows(rows);
        // Recomputed here rather than in MapToLootRecord so it agrees with the rows above — an
        // overridden item has to raise the kill's total, or the feed's tier pre-filter and every GP
        // aggregate would still be reading the pre-override figure.
        record.TotalValue = rows.Sum(r => (long)r.Quantity * r.Price);
    }
}
