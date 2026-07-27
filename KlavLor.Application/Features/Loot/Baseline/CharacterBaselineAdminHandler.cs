using KlavLor.Application.Features.Loot.Special;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.Baseline;

// Backs the admin "baseline kill counts" panel: set the seed KC for a character at a source so an
// onboarded player who already ground the content starts from a realistic number.
public sealed class CharacterBaselineAdminHandler(
    ICharacterSourceBaselineRepository repository,
    IGameCharacterRepository characters)
{
    public async Task<List<SpecialLootCharacterOption>> GetCharacters()
    {
        var chars = await characters.GetSelectable();
        return chars.Select(c => new SpecialLootCharacterOption(c.Id, c.GetEffectiveName())).ToList();
    }

    public Task<List<CharacterBaselineRow>> List() => repository.List();

    public async Task<List<CharacterBaselineRow>> Set(int characterId, string sourceName, int baselineKc)
    {
        var source = (sourceName ?? "").Trim();
        if (source.Length > 0)
            await repository.Upsert(characterId, source, Math.Max(0, baselineKc));
        return await repository.List();
    }
}
