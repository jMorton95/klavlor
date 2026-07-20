using Microsoft.EntityFrameworkCore;
using KlavLor.Domain.Interfaces.Repositories;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class LootDerivationRepository(DataContext dataContext) : ILootDerivationRepository
{
    public async Task<bool> HasRecordsNeedingDerivation(IReadOnlyCollection<string> sources, int currentVersion)
    {
        if (sources.Count == 0) return false;
        return await dataContext.LootRecords.AnyAsync(r =>
            sources.Contains(r.SourceName) &&
            (r.EffectiveKillsVersion == null || r.EffectiveKillsVersion < currentVersion));
    }

    public async Task<IReadOnlyList<LootDerivationRecord>> GetBatchNeedingDerivation(
        IReadOnlyCollection<string> sources, int currentVersion, int batchSize)
    {
        if (sources.Count == 0) return [];
        return await dataContext.LootRecords.AsNoTracking()
            .Where(r => sources.Contains(r.SourceName) &&
                        (r.EffectiveKillsVersion == null || r.EffectiveKillsVersion < currentVersion))
            .OrderBy(r => r.Id)
            .Take(batchSize)
            .Select(r => new LootDerivationRecord(r.Id, r.SourceName, r.DropsJson))
            .ToListAsync();
    }

    public async Task ApplyDerivations(IReadOnlyCollection<LootDerivationResult> results, int version)
    {
        if (results.Count == 0) return;

        // Group by the computed value so records sharing a depth update in one statement, then
        // set only EffectiveKills + the version marker. ExecuteUpdate bypasses the change
        // tracker and the audit interceptor, so SavedAt / SavedById / RowVersion on these
        // historical rows are left exactly as they were — a minimal, non-destructive write.
        foreach (var group in results.GroupBy(r => r.EffectiveKills))
        {
            var value = group.Key;
            var ids = group.Select(r => r.Id).ToList();
            await dataContext.LootRecords
                .Where(r => ids.Contains(r.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.EffectiveKills, value)
                    .SetProperty(r => r.EffectiveKillsVersion, version));
        }
    }
}
