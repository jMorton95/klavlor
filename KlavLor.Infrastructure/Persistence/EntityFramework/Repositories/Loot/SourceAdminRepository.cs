using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Common.Exceptions;
using KlavLor.Application.Features.Maintenance;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class SourceAdminRepository(DataContext dataContext, ILogger<SourceAdminRepository> logger)
    : ISourceAdminRepository
{
    public async Task<List<SourceNameRow>> Search(string? term, int limit)
    {
        try
        {
            var query = dataContext.LootRecords.AsQueryable();

            if (!string.IsNullOrWhiteSpace(term))
            {
                var pattern = $"%{term.Trim()}%";
                query = query.Where(r => EF.Functions.ILike(r.SourceName, pattern));
            }

            // Project to an anonymous type so EF translates the GROUP BY / COUNT cleanly;
            // map to the DTO in memory (a record constructor inside the SQL projection
            // isn't translatable).
            var rows = await query
                .GroupBy(r => r.SourceName)
                .Select(g => new { SourceName = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(limit)
                .ToListAsync();

            return rows.Select(r => new SourceNameRow(r.SourceName, r.Count)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search source names (term {Term})", term);
            throw new RepositoryException("Failed to search source names", ex);
        }
    }

    public async Task<SourceRenamePreview> PreviewRename(string from, string to)
    {
        try
        {
            var recordsToMove = await dataContext.LootRecords.CountAsync(r => r.SourceName == from);
            var targetExisting = await dataContext.LootRecords.CountAsync(r => r.SourceName == to);
            var dropRatesAffected = await dataContext.DropRates.CountAsync(d => d.SourceName == from);
            var hasIcon = await dataContext.SourceIcons.AnyAsync(i => i.SourceName == from);

            return new SourceRenamePreview(
                From: from,
                To: to,
                RecordsToMove: recordsToMove,
                IsMerge: targetExisting > 0,
                TargetExistingRecords: targetExisting,
                DropRatesAffected: dropRatesAffected,
                HasIcon: hasIcon,
                IsNoop: false,
                NoopReason: null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to preview rename '{From}' to '{To}'", from, to);
            throw new RepositoryException("Failed to preview source rename", ex);
        }
    }

    public async Task<int> RenameSource(string from, string to)
    {
        try
        {
            await using var transaction = await dataContext.Database.BeginTransactionAsync();

            // Repoint loot (the real data) to the canonical name.
            var moved = await dataContext.LootRecords
                .Where(r => r.SourceName == from)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.SourceName, to));

            // Drop the variant's derived rows; they're re-fetched/re-derived for the
            // canonical name (and this avoids the unique-constraint clashes a merge into
            // an existing target would otherwise cause).
            await dataContext.DropRates
                .Where(d => d.SourceName == from)
                .ExecuteDeleteAsync();

            await dataContext.SourceIcons
                .Where(i => i.SourceName == from)
                .ExecuteDeleteAsync();

            await transaction.CommitAsync();
            return moved;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rename source '{From}' to '{To}'", from, to);
            throw new RepositoryException("Failed to rename source", ex);
        }
    }
}
