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

            return await query
                .GroupBy(r => r.SourceName)
                .Select(g => new SourceNameRow(g.Key, g.Count()))
                .OrderByDescending(r => r.LootCount)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search source names (term {Term})", term);
            throw new RepositoryException("Failed to search source names", ex);
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
