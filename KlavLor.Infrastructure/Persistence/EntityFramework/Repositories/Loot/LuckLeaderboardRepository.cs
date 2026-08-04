using Microsoft.EntityFrameworkCore;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class LuckLeaderboardRepository(DataContext dataContext) : ILuckLeaderboardRepository
{
    public async Task<IReadOnlyList<(int Id, string Name)>> GetVisibleCharacters()
    {
        var rows = await dataContext.GameCharacters
            .Where(gc => gc.IsVisible && !gc.IsAdminHidden && !gc.IsLeagues && gc.DisplayName != null)
            .Select(gc => new { gc.Id, gc.DisplayName })
            .ToListAsync();
        return rows.Select(r => (r.Id, r.DisplayName!)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetSourcesForCharacter(int characterId) =>
        await dataContext.LootRecords
            .Where(r => r.GameCharacterId == characterId)
            .Select(r => r.SourceName)
            .Distinct()
            .ToListAsync();

    public async Task<long> NextGeneration()
    {
        var max = await dataContext.LuckLeaderboardEntries.MaxAsync(e => (long?)e.Generation);
        return (max ?? 0) + 1;
    }

    public async Task InsertEntries(IReadOnlyCollection<LuckLeaderboardEntry> entries)
    {
        if (entries.Count == 0) return;
        dataContext.LuckLeaderboardEntries.AddRange(entries);
        await dataContext.SaveChangesAsync();
        // Detach the just-saved rows so the tracker doesn't grow across the hourly sweep.
        dataContext.ChangeTracker.Clear();
    }

    public async Task PublishGeneration(long generation)
    {
        var meta = await dataContext.LuckLeaderboardMeta.FirstOrDefaultAsync();
        if (meta is null)
        {
            dataContext.LuckLeaderboardMeta.Add(new LuckLeaderboardMeta
            {
                CurrentGeneration = generation,
                RefreshedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            meta.CurrentGeneration = generation;
            meta.RefreshedAt = DateTimeOffset.UtcNow;
        }
        await dataContext.SaveChangesAsync();

        // Pointer has flipped — drop every superseded generation.
        await dataContext.LuckLeaderboardEntries
            .Where(e => e.Generation != generation)
            .ExecuteDeleteAsync();
        dataContext.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<LuckLeaderboardEntry>> GetBoard(LeaderboardBoard board, int limit)
    {
        var meta = await dataContext.LuckLeaderboardMeta.AsNoTracking().FirstOrDefaultAsync();
        if (meta is null) return [];

        return await dataContext.LuckLeaderboardEntries.AsNoTracking()
            .Where(e => e.Generation == meta.CurrentGeneration && e.Board == board)
            .OrderByDescending(e => e.Tier)
            // Within a tier, the rarer grind wins. Keyed on ExpectedKc rather than
            // RarityDenominator because a depth-modelled source (Doom) has no flat denominator and
            // stores 0, which lost it every single tie-break and buried its entries at the bottom of
            // their tier. ExpectedKc is populated for every source and equals the denominator for an
            // ordinary single-roll drop, so flat-rate ordering is unchanged.
            .ThenByDescending(e => e.ExpectedKc)
            .ThenByDescending(e => e.Multiple)
            .Take(limit)
            .ToListAsync();
    }
}
