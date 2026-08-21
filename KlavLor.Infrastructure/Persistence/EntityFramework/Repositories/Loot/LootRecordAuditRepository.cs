using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

/// <summary>
/// The admin record-audit queries: narrow to a character and source, page the records, delete one.
///
/// Deliberately narrow. Every read here is already filtered to a single character and a single
/// source before any matching happens, so the fuzzy search never runs across the whole table.
/// </summary>
internal sealed class LootRecordAuditRepository(DataContext dataContext, ILogger<LootRecordAuditRepository> logger)
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
        int gameCharacterId, string sourceName, string? term, int page, int pageSize)
    {
        try
        {
            var records = dataContext.LootRecords.AsNoTracking()
                .Where(r => r.GameCharacterId == gameCharacterId && r.SourceName == sourceName);

            // The search matches the ITEMS in a record, not the record itself — the admin is
            // hunting "which kill logged the thing that shouldn't be here", and the source is
            // already fixed by the time they type. Exact substring OR trigram similarity, so a
            // half-remembered or slightly mistyped name still finds it.
            if (!string.IsNullOrWhiteSpace(term))
            {
                records = records.Where(r => dataContext.LootDrops.Any(d =>
                    d.LootRecordId == r.Id
                    && (EF.Functions.ILike(d.Name, "%" + term + "%")
                        || EF.Functions.TrigramsSimilarity(d.Name, term) >= SimilarityFloor)));
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
                    r.ExcludedFromLuck
                })
                .ToListAsync();

            // Drops come from the LootDrops projection rather than DropsJson because it already
            // holds the EFFECTIVE price — what the rest of the site shows for these items.
            var ids = rows.Select(r => r.Id).ToList();
            var drops = ids.Count == 0
                ? []
                : await dataContext.LootDrops.AsNoTracking()
                    .Where(d => ids.Contains(d.LootRecordId))
                    .OrderByDescending(d => (long)d.Quantity * d.Price)
                    .Select(d => new { d.LootRecordId, d.Name, d.Quantity, d.Price })
                    .ToListAsync();

            var byRecord = drops
                .GroupBy(d => d.LootRecordId)
                .ToDictionary(g => g.Key, g => g.Select(d => new AuditRecordDrop(d.Name, d.Quantity, d.Price)).ToList());

            return new AuditRecordPage(
                rows.Select(r => new AuditRecordRow(
                    r.Id, r.SourceName, r.OccurredAt, r.KillCount, r.TotalValue, r.IsImported, r.ContentHash,
                    r.ExcludedFromLuck, byRecord.TryGetValue(r.Id, out var d) ? d : [])).ToList(),
                page, pageSize, total);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search audit records for character {CharacterId}", gameCharacterId);
            throw new RepositoryException("Failed to search records", ex);
        }
    }

    public async Task<DeletedRecordInfo?> SetLuckExclusion(int recordId, bool excluded)
    {
        try
        {
            var record = await dataContext.LootRecords.FirstOrDefaultAsync(r => r.Id == recordId);
            if (record is null) return null;

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

    public async Task<DeletedRecordInfo?> Delete(int recordId)
    {
        try
        {
            var record = await dataContext.LootRecords.FirstOrDefaultAsync(r => r.Id == recordId);
            if (record is null) return null;

            // Read the item names BEFORE the delete — they are what the caller needs to invalidate
            // the per-item global pages, and the cascade takes them with the record.
            var itemNames = await dataContext.LootDrops.AsNoTracking()
                .Where(d => d.LootRecordId == recordId)
                .Select(d => d.Name)
                .ToListAsync();

            var characterId = record.GameCharacterId ?? 0;
            var sourceName = record.SourceName;

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
}
