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
    IUserRepository userRepository,
    ICollectionLogCache collectionLogCache,
    IMemoryCache memoryCache,
    IDropRateRepository dropRateRepository,
    ICharacterDelveDepthRepository delveDepthRepository,
    SourceLootService sourceLoot)
{
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

        if (ShouldPublishToFeed(record, character))
            await PublishToFeed(userId.Value, record, character);
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

        var liveRecords = parsedItems
            .Where(p => ShouldPublishToFeed(p.Parsed.Record, p.Character))
            .Select(p => (p.Parsed.Record, p.Character))
            .ToList();
        if (liveRecords.Count > 0)
        {
            var user = await userRepository.GetById(userId.Value);
            var userName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";
            foreach (var (record, character) in liveRecords)
            {
                await PublishRecordToFeed(userName, record, character);
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

    private static bool ShouldPublishToFeed(LootRecord record, GameCharacter? character)
    {
        if (!IsCharacterVisible(character))
            return false;

        // Imported records only publish if any single drop qualifies for Rare+ (1M+) to avoid flooding.
        if (record.IsImported)
        {
            var drops = JsonSerializer.Deserialize<List<LootDrop>>(record.DropsJson) ?? [];
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

    private async Task PublishToFeed(int userId, LootRecord record, GameCharacter? character)
    {
        var user = await userRepository.GetById(userId);
        var userName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";
        await PublishRecordToFeed(userName, record, character);
    }

    private async Task PublishRecordToFeed(string userName, LootRecord record, GameCharacter? character)
    {
        var drops = JsonSerializer.Deserialize<List<LootDrop>>(record.DropsJson) ?? [];

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
        int? ordinal = null;
        if (character is not null && record.Id > 0)
            ordinal = await lootRecordRepository.GetKillOrdinal(
                character.Id, record.SourceName, record.OccurredAt, record.Id);

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
                OccurredAt: record.OccurredAt);
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

    private static void FinalizeDrops(LootRecord record, List<LootDrop> drops)
    {
        // DropsJson stays the canonical record; the normalised LootDrop rows are written
        // from the same finalised list so both representations agree. EF inserts the child
        // rows when the LootRecord is added (single- or batch-ingest both funnel here).
        record.DropsJson = JsonSerializer.Serialize(drops);
        record.ReplaceDropRows(drops.Select(d => new LootDropRow
        {
            ItemId = d.ItemId,
            Name = d.Name,
            Quantity = d.Quantity,
            Price = d.Price,
            IsFirstTime = d.IsFirstTime,
            IsSpecial = d.IsSpecial
        }));
    }
}
