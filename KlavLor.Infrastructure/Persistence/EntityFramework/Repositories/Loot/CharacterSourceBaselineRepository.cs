using Microsoft.EntityFrameworkCore;
using KlavLor.Application.Features.Loot.Baseline;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class CharacterSourceBaselineRepository(DataContext dataContext) : ICharacterSourceBaselineRepository
{
    public async Task<int> GetBaseline(int characterId, string sourceName)
    {
        return await dataContext.CharacterSourceBaselines
            .AsNoTracking()
            .Where(b => b.GameCharacterId == characterId && b.SourceName == sourceName)
            .Select(b => (int?)b.BaselineKc)
            .FirstOrDefaultAsync() ?? 0;
    }

    public async Task Upsert(int characterId, string sourceName, int baselineKc)
    {
        var existing = await dataContext.CharacterSourceBaselines
            .FirstOrDefaultAsync(b => b.GameCharacterId == characterId && b.SourceName == sourceName);

        if (baselineKc <= 0)
        {
            if (existing is not null)
            {
                dataContext.CharacterSourceBaselines.Remove(existing);
                await dataContext.SaveChangesAsync();
            }
            return;
        }

        if (existing is null)
            dataContext.CharacterSourceBaselines.Add(new CharacterSourceBaseline
            {
                GameCharacterId = characterId,
                SourceName = sourceName,
                BaselineKc = baselineKc
            });
        else
            existing.BaselineKc = baselineKc;

        await dataContext.SaveChangesAsync();
    }

    public async Task<List<CharacterBaselineRow>> List()
    {
        return await dataContext.CharacterSourceBaselines
            .AsNoTracking()
            .Join(dataContext.GameCharacters, b => b.GameCharacterId, c => c.Id, (b, c) => new { b, c })
            .OrderBy(x => x.c.DisplayName).ThenBy(x => x.b.SourceName)
            .Select(x => new CharacterBaselineRow(x.b.GameCharacterId, x.c.DisplayName ?? "Unknown", x.b.SourceName, x.b.BaselineKc))
            .ToListAsync();
    }
}
