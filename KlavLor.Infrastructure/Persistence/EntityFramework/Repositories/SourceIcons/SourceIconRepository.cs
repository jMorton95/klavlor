using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.SourceIcons;

internal sealed class SourceIconRepository(DataContext dataContext, ILogger<SourceIconRepository> logger) : ISourceIconRepository
{
    public async Task<SourceIcon?> GetBySourceName(string sourceName)
    {
        try
        {
            var normalized = sourceName.Trim().ToLower();
            return await dataContext.SourceIcons
                .FirstOrDefaultAsync(s => s.SourceName.ToLower() == normalized);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get source icon by name {Name}", sourceName);
            throw new RepositoryException("Failed to get source icon", ex);
        }
    }

    public async Task<List<string>> FindUncataloguedSources(int limit)
    {
        try
        {
            var results = await dataContext.Database
                .SqlQueryRaw<UncataloguedSource>(
                    """
                    SELECT DISTINCT lr."SourceName"
                    FROM "LootRecords" lr
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "SourceIcons" si WHERE LOWER(si."SourceName") = LOWER(lr."SourceName")
                    )
                    LIMIT {0}
                    """, limit)
                .ToListAsync();

            return results.Select(r => r.SourceName).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to find uncatalogued sources");
            throw new RepositoryException("Failed to find uncatalogued sources", ex);
        }
    }

    public async Task<List<SourceIcon>> GetPendingIcons(int limit)
    {
        try
        {
            return await dataContext.SourceIcons
                .Where(s => s.CachedImageId == null && s.FailCount < 3
                    && (s.LastAttemptAt == null || s.LastAttemptAt < DateTimeOffset.UtcNow.AddMinutes(-30)))
                .OrderBy(s => s.FailCount)
                .ThenBy(s => s.Id)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get pending source icons");
            throw new RepositoryException("Failed to get pending source icons", ex);
        }
    }

    public async Task Save(SourceIcon icon)
    {
        try
        {
            if (icon.Id == 0)
                dataContext.SourceIcons.Add(icon);
            else
                dataContext.SourceIcons.Update(icon);

            await dataContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save source icon for {Name}", icon.SourceName);
            throw new RepositoryException("Failed to save source icon", ex);
        }
    }

    public async Task SaveRange(List<SourceIcon> icons)
    {
        try
        {
            var newIcons = icons.Where(i => i.Id == 0).ToList();
            var existingIcons = icons.Where(i => i.Id != 0).ToList();

            foreach (var icon in newIcons)
            {
                await dataContext.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO "SourceIcons" ("SourceName", "CachedImageId", "FailCount", "LastAttemptAt")
                    VALUES ({icon.SourceName}, {icon.CachedImageId}, {icon.FailCount}, {icon.LastAttemptAt})
                    ON CONFLICT ("SourceName") DO NOTHING
                    """);
            }

            if (existingIcons.Count > 0)
            {
                dataContext.SourceIcons.UpdateRange(existingIcons);
                await dataContext.SaveChangesAsync();
            }
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save source icons batch");
            throw new RepositoryException("Failed to save source icons batch", ex);
        }
    }
}

internal sealed class UncataloguedSource
{
    public string SourceName { get; set; } = "";
}
