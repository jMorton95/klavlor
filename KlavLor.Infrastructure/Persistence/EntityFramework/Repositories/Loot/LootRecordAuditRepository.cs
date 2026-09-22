using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

/// <summary>
/// The admin record-audit queries: narrow to a character, page the records, and repair one.
///
/// Every read here is bound to a single character before any matching happens, so the fuzzy search
/// never runs across the whole table.
/// </summary>
internal sealed class LootRecordAuditRepository(
    DataContext dataContext,
    ILootRecordRepository lootRecords,
    IItemValueOverrideCache itemValues,
    ILogger<LootRecordAuditRepository> logger)
    : ILootRecordAuditRepository
{
    /// <summary>
    /// Trigram similarity floor for the fuzzy match. 0.3 is Postgres's own default and tolerates a
    /// character or two of typo on a normal item name without matching everything.
    /// </summary>
    private const double SimilarityFloor = 0.3;

    public async Task<List<AuditSourceOption>> GetSources(int gameCharacterId)
    {
        try
        {
            // Projected to an anonymous type in SQL and mapped afterwards: EF cannot translate the
            // construction of a positional record inside a GroupBy projection.
            var grouped = await dataContext.LootRecords.AsNoTracking()
                .Where(r => r.GameCharacterId == gameCharacterId)
                .GroupBy(r => r.SourceName)
                .Select(g => new { SourceName = g.Key, RecordCount = g.Count() })
                // Busiest first: the source being audited is nearly always one they farm.
                .OrderByDescending(o => o.RecordCount)
                .ThenBy(o => o.SourceName)
                .ToListAsync();

            return grouped.Select(g => new AuditSourceOption(g.SourceName, g.RecordCount)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list audit sources for character {CharacterId}", gameCharacterId);
            throw new RepositoryException("Failed to list sources", ex);
        }
    }

    public async Task<AuditRecordPage> Search(
        int gameCharacterId, string? sourceName, string? term, int page, int pageSize)
    {
        try
        {
            var records = dataContext.LootRecords.AsNoTracking()
                .Where(r => r.GameCharacterId == gameCharacterId);

            // A source narrows the search; it no longer gates it. Searching an ITEM across every
            // source is how you find a mis-attributed drop, because the source it was filed under is
            // precisely the thing you cannot guess — that is what makes it mis-attributed. The
            // character bound above still keeps this off the whole table.
            var source = (sourceName ?? "").Trim();
            if (source.Length > 0)
                records = records.Where(r => r.SourceName == source);

            // The search matches the ITEMS in a record, not the record itself — the admin is
            // hunting "which kill logged the thing that shouldn't be here". Exact substring OR
            // trigram similarity, so a half-remembered or slightly mistyped name still finds it.
            // Both use real indexes (the gin_trgm on LootDrops."Name"), which is why this searches
            // the projection and the blacklist table rather than unrolling DropsJson per row.
            if (!string.IsNullOrWhiteSpace(term))
            {
                var needle = term.Trim();
                records = records.Where(r =>
                    dataContext.LootDrops.Any(d =>
                        d.LootRecordId == r.Id
                        && (EF.Functions.ILike(d.Name, "%" + needle + "%")
                            || EF.Functions.TrigramsSimilarity(d.Name, needle) >= SimilarityFloor))
                    // A blacklisted drop has left the projection, so the clause above can no longer
                    // see it. Without this second arm the panel could not find what it had itself
                    // hidden, and a blacklist would be unreviewable and impossible to lift by search.
                    || dataContext.BlacklistedLootDrops.Any(b =>
                        b.LootRecordId == r.Id
                        && (EF.Functions.ILike(b.ItemName, "%" + needle + "%")
                            || EF.Functions.TrigramsSimilarity(b.ItemName, needle) >= SimilarityFloor)));
            }

            var total = await records.CountAsync();

            var rows = await records
                // Newest first: a mis-attributed drop is nearly always one the user just reported.
                .OrderByDescending(r => r.OccurredAt)
                .ThenByDescending(r => r.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id, r.SourceName, r.OccurredAt, r.KillCount, r.TotalValue, r.IsImported, r.ContentHash,
                    r.ExcludedFromLuck, r.DropsJson
                })
                .ToListAsync();

            // Drops come from the CANONICAL DropsJson rather than the LootDrops projection, and this
            // panel is the only place that does. Everywhere else reads the projection, which is
            // exactly how a blacklisted drop disappears; here it must still be visible, marked, so
            // the admin can see what they hid and put it back. Prices are re-applied from the
            // override cache so the figures still match the rest of the site.
            var ids = rows.Select(r => r.Id).ToList();
            var blacklisted = ids.Count == 0
                ? []
                : await dataContext.BlacklistedLootDrops.AsNoTracking()
                    .Where(b => ids.Contains(b.LootRecordId))
                    .Select(b => new { b.LootRecordId, b.ItemId, b.ItemName })
                    .ToListAsync();

            var blacklistedKeys = blacklisted
                .Select(b => (b.LootRecordId, b.ItemId, Name: b.ItemName.ToLowerInvariant()))
                .ToHashSet();

            return new AuditRecordPage(
                rows.Select(r => new AuditRecordRow(
                    r.Id, r.SourceName, r.OccurredAt, r.KillCount, r.TotalValue, r.IsImported, r.ContentHash,
                    r.ExcludedFromLuck,
                    ParseDrops(r.DropsJson)
                        .Select(d => new AuditRecordDrop(
                            d.Name,
                            d.ItemId,
                            d.Quantity,
                            itemValues.GetPrice(d.ItemId, d.Price),
                            blacklistedKeys.Contains((r.Id, d.ItemId, d.Name.ToLowerInvariant()))))
                        .OrderByDescending(d => d.Quantity * d.Price)
                        .ToList())).ToList(),
                page, pageSize, total);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search audit records for character {CharacterId}", gameCharacterId);
            throw new RepositoryException("Failed to search records", ex);
        }
    }

    /// <summary>
    /// Refresh a tracked record's concurrency token before writing to it, or report that it has gone.
    /// </summary>
    /// <remarks>
    /// The projection rebuild re-derives <c>LootRecords.TotalValue</c> with raw set-based SQL, which
    /// in Postgres moves the row's <c>xmin</c> — the EF concurrency token — behind the change
    /// tracker's back. A <see cref="LootRecord"/> already tracked in this scope is then holding a
    /// stale token, and its next tracked write matches zero rows and throws
    /// <c>DbUpdateConcurrencyException</c>. That is a false conflict: nobody edited the record, a
    /// DERIVED column was recomputed, and the admin's decision has no way to satisfy the check.
    ///
    /// Reloading rather than suppressing keeps the token honest for a genuine conflict. A row that
    /// has actually gone comes back detached, which is reported as "no longer exists" — the same
    /// answer the caller already gives for a record that was never found.
    /// </remarks>
    private async Task<bool> StillExists(LootRecord record)
    {
        var entry = dataContext.Entry(record);
        await entry.ReloadAsync();
        return entry.State != EntityState.Detached;
    }

    public async Task<DeletedRecordInfo?> SetLuckExclusion(int recordId, bool excluded)
    {
        try
        {
            var record = await dataContext.LootRecords.FirstOrDefaultAsync(r => r.Id == recordId);
            if (record is null || !await StillExists(record)) return null;

            // Item names are read whether or not the flag actually moves: the caller invalidates the
            // per-item global pages from them, and an idempotent no-op still has to return a
            // complete answer rather than a half-populated one.
            var itemNames = await dataContext.LootDrops.AsNoTracking()
                .Where(d => d.LootRecordId == recordId)
                .Select(d => d.Name)
                .ToListAsync();

            if (record.ExcludedFromLuck != excluded)
            {
                record.ExcludedFromLuck = excluded;
                await dataContext.SaveChangesAsync();

                logger.LogInformation(
                    "Admin set luck exclusion {Excluded} on loot record {RecordId} ({Source}, character {CharacterId})",
                    excluded, recordId, record.SourceName, record.GameCharacterId);
            }

            return new DeletedRecordInfo(record.GameCharacterId ?? 0, record.SourceName, itemNames);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set luck exclusion on loot record {RecordId}", recordId);
            throw new RepositoryException("Failed to update record", ex);
        }
    }

    public async Task<DeletedRecordInfo?> SetDropBlacklist(
        int recordId, int itemId, string itemName, bool blacklisted, string? reason)
    {
        try
        {
            var record = await dataContext.LootRecords.AsNoTracking()
                .Where(r => r.Id == recordId)
                .Select(r => new { r.Id, r.GameCharacterId, r.SourceName })
                .FirstOrDefaultAsync();
            if (record is null) return null;

            // Matched case-insensitively on both halves of the key, the same rule the projection SQL
            // and the cache apply. Spelling it differently here would let a row be written that
            // nothing can ever find to lift.
            var existing = await dataContext.BlacklistedLootDrops
                .FirstOrDefaultAsync(b => b.LootRecordId == recordId
                                          && b.ItemId == itemId
                                          && b.ItemName.ToLower() == itemName.ToLower());

            if (blacklisted && existing is null)
            {
                dataContext.BlacklistedLootDrops.Add(new BlacklistedLootDrop
                {
                    LootRecordId = recordId,
                    ItemId = itemId,
                    // Stored as the drop spells it, for display. Matching lowercases both sides.
                    ItemName = itemName,
                    Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
                });
                await dataContext.SaveChangesAsync();

                logger.LogInformation(
                    "Admin blacklisted drop {ItemName} ({ItemId}) on loot record {RecordId} ({Source}, character {CharacterId})",
                    itemName, itemId, recordId, record.SourceName, record.GameCharacterId);
            }
            else if (!blacklisted && existing is not null)
            {
                dataContext.BlacklistedLootDrops.Remove(existing);
                await dataContext.SaveChangesAsync();

                logger.LogInformation(
                    "Admin restored blacklisted drop {ItemName} ({ItemId}) on loot record {RecordId}",
                    itemName, itemId, recordId);
            }

            // Re-derive the record's projection and total from the canonical DropsJson. THIS is what
            // actually hides or restores the drop: the blacklist row above is only the decision, and
            // on its own changes nothing any page reads. Run unconditionally, including for a no-op
            // toggle, so a projection that has drifted for any other reason is repaired rather than
            // left disagreeing with a blacklist that looks correct.
            await lootRecords.RebuildDropsForRecord(recordId);

            // Read AFTER the rebuild: the caller invalidates per-item global pages from these, and
            // the item that just changed visibility is the one that most needs it. A restore puts it
            // back into the projection, so reading after covers both directions — except a
            // blacklist, where the row has gone, so the item name is added explicitly.
            var itemNames = await dataContext.LootDrops.AsNoTracking()
                .Where(d => d.LootRecordId == recordId)
                .Select(d => d.Name)
                .ToListAsync();
            itemNames.Add(itemName);

            return new DeletedRecordInfo(record.GameCharacterId ?? 0, record.SourceName, itemNames);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set drop blacklist on loot record {RecordId}, item {ItemId}", recordId, itemId);
            throw new RepositoryException("Failed to update drop", ex);
        }
    }

    public async Task<IReadOnlyList<BlacklistedDropValue>> GetAllBlacklistedDrops()
    {
        return await dataContext.BlacklistedLootDrops.AsNoTracking()
            .Select(b => new BlacklistedDropValue(b.LootRecordId, b.ItemId, b.ItemName))
            .ToListAsync();
    }

    public async Task<DeletedRecordInfo?> Delete(int recordId, string? reason)
    {
        try
        {
            var record = await dataContext.LootRecords.FirstOrDefaultAsync(r => r.Id == recordId);
            if (record is null || !await StillExists(record)) return null;

            // Read the item names BEFORE the delete — they are what the caller needs to invalidate
            // the per-item global pages, and the cascade takes them with the record. From the
            // canonical JSON rather than the projection so a blacklisted drop is still accounted
            // for: it is leaving too, and its global page was built while it was still visible.
            var drops = ParseDrops(record.DropsJson);
            var itemNames = drops.Select(d => d.Name).ToList();

            var characterId = record.GameCharacterId ?? 0;
            var sourceName = record.SourceName;

            var characterName = characterId == 0
                ? "Unknown"
                : await dataContext.GameCharacters.AsNoTracking()
                      .Where(c => c.Id == characterId)
                      .Select(c => c.DisplayName ?? c.RuneLiteId)
                      .FirstOrDefaultAsync() ?? "Unknown";

            // Logged BEFORE the delete, in the same SaveChanges, so a log row can never describe a
            // deletion that did not happen and a deletion can never go unlogged. Deletion stays
            // irreversible; this records that it was a decision rather than a sync that never ran.
            dataContext.DeletedLootRecordLogs.Add(new DeletedLootRecordLog
            {
                LootRecordId = recordId,
                GameCharacterId = record.GameCharacterId,
                CharacterName = Truncate(characterName, 100),
                SourceName = sourceName,
                OccurredAt = record.OccurredAt,
                KillCount = record.KillCount,
                TotalValue = record.TotalValue,
                DropsSummary = Truncate(SummariseDrops(drops), 1000),
                Reason = string.IsNullOrWhiteSpace(reason) ? null : Truncate(reason.Trim(), 500)
            });

            dataContext.LootRecords.Remove(record);
            await dataContext.SaveChangesAsync();

            logger.LogInformation(
                "Admin deleted loot record {RecordId} ({Source}, character {CharacterId}, {DropCount} drops)",
                recordId, sourceName, characterId, itemNames.Count);

            return new DeletedRecordInfo(characterId, sourceName, itemNames);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete loot record {RecordId}", recordId);
            throw new RepositoryException("Failed to delete record", ex);
        }
    }

    public async Task<List<AuditModification>> GetModifications(int limit)
    {
        try
        {
            // Three reads rather than one UNION: the sources share almost no columns, and each is
            // bounded to `limit` before anything is merged, so the whole list is a few dozen rows.
            var blacklisted = await dataContext.BlacklistedLootDrops.AsNoTracking()
                .OrderByDescending(b => b.SavedAt).ThenByDescending(b => b.Id)
                .Take(limit)
                .Select(b => new
                {
                    b.ItemId,
                    b.ItemName,
                    b.Reason,
                    b.SavedAt,
                    ChangedBy = b.SavedBy != null ? b.SavedBy.FirstName + " " + b.SavedBy.LastName : null,
                    RecordId = b.LootRecordId,
                    b.LootRecord!.GameCharacterId,
                    b.LootRecord.SourceName,
                    b.LootRecord.OccurredAt,
                    b.LootRecord.KillCount,
                    b.LootRecord.TotalValue,
                    CharacterName = b.LootRecord.GameCharacter != null
                        ? b.LootRecord.GameCharacter.DisplayName ?? b.LootRecord.GameCharacter.RuneLiteId
                        : "Unknown"
                })
                .ToListAsync();

            var excluded = await dataContext.LootRecords.AsNoTracking()
                .Where(r => r.ExcludedFromLuck)
                .OrderByDescending(r => r.SavedAt).ThenByDescending(r => r.Id)
                .Take(limit)
                .Select(r => new
                {
                    RecordId = r.Id,
                    r.GameCharacterId,
                    r.SourceName,
                    r.OccurredAt,
                    r.KillCount,
                    r.TotalValue,
                    r.SavedAt,
                    ChangedBy = r.SavedBy != null ? r.SavedBy.FirstName + " " + r.SavedBy.LastName : null,
                    CharacterName = r.GameCharacter != null
                        ? r.GameCharacter.DisplayName ?? r.GameCharacter.RuneLiteId
                        : "Unknown"
                })
                .ToListAsync();

            var deleted = await dataContext.DeletedLootRecordLogs.AsNoTracking()
                .OrderByDescending(d => d.SavedAt).ThenByDescending(d => d.Id)
                .Take(limit)
                .Select(d => new
                {
                    d.LootRecordId,
                    d.GameCharacterId,
                    d.CharacterName,
                    d.SourceName,
                    d.OccurredAt,
                    d.KillCount,
                    d.TotalValue,
                    d.DropsSummary,
                    d.Reason,
                    d.SavedAt,
                    ChangedBy = d.SavedBy != null ? d.SavedBy.FirstName + " " + d.SavedBy.LastName : null
                })
                .ToListAsync();

            return blacklisted
                .Select(b => new AuditModification(
                    AuditModificationKind.BlacklistedDrop, b.RecordId, b.GameCharacterId, b.CharacterName,
                    b.SourceName, b.OccurredAt, b.KillCount, b.TotalValue, b.ItemName, b.ItemId, b.Reason,
                    b.ChangedBy, b.SavedAt, Restorable: true))
                .Concat(excluded.Select(e => new AuditModification(
                    AuditModificationKind.LuckExcluded, e.RecordId, e.GameCharacterId, e.CharacterName,
                    e.SourceName, e.OccurredAt, e.KillCount, e.TotalValue, "whole record", ItemId: 0,
                    Reason: null, e.ChangedBy, e.SavedAt, Restorable: true)))
                .Concat(deleted.Select(d => new AuditModification(
                    AuditModificationKind.Deleted, d.LootRecordId, d.GameCharacterId, d.CharacterName,
                    d.SourceName, d.OccurredAt, d.KillCount, d.TotalValue, d.DropsSummary, ItemId: 0,
                    d.Reason, d.ChangedBy, d.SavedAt, Restorable: false)))
                .OrderByDescending(m => m.ChangedAt)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list record-audit modifications");
            throw new RepositoryException("Failed to list modifications", ex);
        }
    }

    private static List<LootDrop> ParseDrops(string? dropsJson)
    {
        if (string.IsNullOrWhiteSpace(dropsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<LootDrop>>(dropsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// "Torva platelegs, Nihil dust x54" — what the kill carried, for the deletion log.
    private static string SummariseDrops(List<LootDrop> drops) =>
        drops.Count == 0
            ? "no drops"
            : string.Join(", ", drops
                .OrderByDescending(d => (long)d.Quantity * d.Price)
                .Select(d => d.Quantity > 1 ? $"{d.Name} x{d.Quantity:N0}" : d.Name));

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
