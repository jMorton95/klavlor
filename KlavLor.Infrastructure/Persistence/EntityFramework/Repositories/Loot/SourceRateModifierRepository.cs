using Microsoft.EntityFrameworkCore;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class SourceRateModifierRepository(DataContext dataContext) : ISourceRateModifierRepository
{
    public async Task<List<SourceRateModifierRow>> Search(string? term, int limit)
    {
        // Blank term: the current modifier list so the admin sees what's configured.
        if (string.IsNullOrWhiteSpace(term))
        {
            return await dataContext.SourceRateModifiers
                .AsNoTracking()
                .OrderBy(m => m.SourceName).ThenBy(m => m.ItemName)
                .Select(m => new SourceRateModifierRow(m.SourceName, m.ItemName, m.Multiplier))
                .ToListAsync();
        }

        // Otherwise: source names that actually have loot, matching the term, each carrying its
        // current source-wide multiplier (1.0 when none) so the admin can pick and adjust.
        var pattern = $"%{term.Trim()}%";
        var sources = await dataContext.LootRecords
            .AsNoTracking()
            .Where(r => EF.Functions.ILike(r.SourceName, pattern))
            .Select(r => r.SourceName)
            .Distinct()
            .OrderBy(s => s)
            .Take(limit)
            .ToListAsync();

        var sourceWide = (await dataContext.SourceRateModifiers
                .AsNoTracking()
                .Where(m => m.ItemName == "")
                .ToListAsync())
            .ToDictionary(m => m.SourceName, m => m.Multiplier, StringComparer.OrdinalIgnoreCase);

        return sources
            .Select(s => new SourceRateModifierRow(s, "", sourceWide.TryGetValue(s, out var mul) ? mul : 1.0))
            .ToList();
    }

    public async Task Upsert(string sourceName, string itemName, double multiplier)
    {
        itemName ??= "";
        var existing = await dataContext.SourceRateModifiers
            .FirstOrDefaultAsync(m => m.SourceName == sourceName && m.ItemName == itemName);

        if (existing is null)
            dataContext.SourceRateModifiers.Add(new SourceRateModifier
            {
                SourceName = sourceName,
                ItemName = itemName,
                Multiplier = multiplier
            });
        else
            existing.Multiplier = multiplier;

        await dataContext.SaveChangesAsync();
    }

    public async Task Delete(string sourceName, string itemName)
    {
        await dataContext.SourceRateModifiers
            .Where(m => m.SourceName == sourceName && m.ItemName == itemName)
            .ExecuteDeleteAsync();
    }

    public async Task<IReadOnlyList<SourceRateModifierValue>> GetAll()
    {
        return await dataContext.SourceRateModifiers
            .AsNoTracking()
            .Select(m => new SourceRateModifierValue(m.SourceName, m.ItemName, m.Multiplier))
            .ToListAsync();
    }
}
