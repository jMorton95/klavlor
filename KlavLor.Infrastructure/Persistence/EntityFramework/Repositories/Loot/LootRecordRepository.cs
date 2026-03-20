using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class LootRecordRepository(DataContext dataContext, ILogger<LootRecordRepository> logger) : ILootRecordRepository
{
    public async Task<bool> SaveLootRecord(LootRecord record)
    {
        try
        {
            dataContext.LootRecords.Add(record);
            return await dataContext.SaveChangesAsync() > 0;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            logger.LogDebug("Duplicate loot record skipped for user {UserId}, hash {Hash}", record.UserId, record.ContentHash);
            dataContext.Entry(record).State = EntityState.Detached;
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save loot record for user {UserId}", record.UserId);
            throw new RepositoryException("Failed to save loot record", ex);
        }
    }

    public async Task<bool> SaveLootRecords(List<LootRecord> records)
    {
        try
        {
            dataContext.LootRecords.AddRange(records);
            return await dataContext.SaveChangesAsync() > 0;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Batch had duplicates — detach all and fall back to individual inserts
            logger.LogDebug("Batch insert hit duplicate constraint, falling back to individual inserts");
            foreach (var record in records)
                dataContext.Entry(record).State = EntityState.Detached;

            var inserted = 0;
            foreach (var record in records)
            {
                try
                {
                    dataContext.LootRecords.Add(record);
                    await dataContext.SaveChangesAsync();
                    inserted++;
                }
                catch (DbUpdateException inner) when (IsUniqueConstraintViolation(inner))
                {
                    dataContext.Entry(record).State = EntityState.Detached;
                }
            }

            logger.LogDebug("Individual fallback inserted {Inserted}/{Total} records", inserted, records.Count);
            return inserted > 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save {Count} loot records", records.Count);
            throw new RepositoryException("Failed to save loot records", ex);
        }
    }

    public async Task<HashSet<string>> FindExistingHashes(int userId, IEnumerable<string> hashes)
    {
        var hashList = hashes.ToList();
        if (hashList.Count == 0)
            return [];

        var existing = await dataContext.LootRecords
            .Where(r => r.UserId == userId && r.ContentHash != null && hashList.Contains(r.ContentHash))
            .Select(r => r.ContentHash!)
            .ToListAsync();

        return existing.ToHashSet();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("duplicate key value violates unique constraint") == true
            || ex.InnerException?.Message.Contains("23505") == true;
    }
}
