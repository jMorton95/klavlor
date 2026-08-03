using Microsoft.EntityFrameworkCore;
using KlavLor.Application.Features.Loot.DelveDepth;
using KlavLor.Application.Interfaces.Repositories;
using KlavLor.Domain.Entities;

namespace KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Loot;

internal sealed class CharacterDelveDepthRepository(DataContext dataContext) : ICharacterDelveDepthRepository
{
    public async Task<int?> GetAverageDepth(int characterId, string sourceName)
    {
        return await dataContext.CharacterDelveDepths
            .AsNoTracking()
            .Where(d => d.GameCharacterId == characterId && d.SourceName == sourceName)
            .Select(d => (int?)d.AverageDepth)
            .FirstOrDefaultAsync();
    }

    public async Task Upsert(int characterId, string sourceName, int averageDepth)
    {
        var existing = await dataContext.CharacterDelveDepths
            .FirstOrDefaultAsync(d => d.GameCharacterId == characterId && d.SourceName == sourceName);

        // Clearing the override restores the assumed default rather than storing a zero.
        if (averageDepth <= 0)
        {
            if (existing is not null)
            {
                dataContext.CharacterDelveDepths.Remove(existing);
                await dataContext.SaveChangesAsync();
            }
            return;
        }

        if (existing is null)
            dataContext.CharacterDelveDepths.Add(new CharacterDelveDepth
            {
                GameCharacterId = characterId,
                SourceName = sourceName,
                AverageDepth = averageDepth
            });
        else
            existing.AverageDepth = averageDepth;

        await dataContext.SaveChangesAsync();
    }

    public async Task<List<CharacterDelveDepthRow>> List()
    {
        return await dataContext.CharacterDelveDepths
            .AsNoTracking()
            .Join(dataContext.GameCharacters, d => d.GameCharacterId, c => c.Id, (d, c) => new { d, c })
            .OrderBy(x => x.c.DisplayName).ThenBy(x => x.d.SourceName)
            .Select(x => new CharacterDelveDepthRow(
                x.d.GameCharacterId, x.c.DisplayName ?? "Unknown", x.d.SourceName, x.d.AverageDepth))
            .ToListAsync();
    }
}
