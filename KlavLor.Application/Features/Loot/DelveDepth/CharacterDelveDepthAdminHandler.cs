using KlavLor.Application.Features.Loot.Special;
using KlavLor.Application.Features.Loot.SourceModels;
using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Loot.DelveDepth;

// Backs the admin "average delve depth" panel. Delve depth can't be read from the loot payload, and
// the only in-payload signal (Demon tear count) isn't depth-proportional, so the strategy assumes a
// default and an admin corrects it per character here. Clearing a row restores the default.
public sealed class CharacterDelveDepthAdminHandler(
    ICharacterDelveDepthRepository repository,
    IGameCharacterRepository characters)
{
    /// <summary>The assumed average used for any character without an override.</summary>
    public static int DefaultDepth => DoomLootStrategy.AssumedAverageDepth;

    /// <summary>Sources whose luck is depth-modelled, so the only ones worth configuring.</summary>
    public static IReadOnlyList<string> DepthSources { get; } = ["Doom of Mokhaiotl"];

    public async Task<List<SpecialLootCharacterOption>> GetCharacters()
    {
        var chars = await characters.GetSelectable();
        return chars.Select(c => new SpecialLootCharacterOption(c.Id, c.GetEffectiveName())).ToList();
    }

    public Task<List<CharacterDelveDepthRow>> List() => repository.List();

    public async Task<List<CharacterDelveDepthRow>> Set(int characterId, string sourceName, int averageDepth)
    {
        var source = (sourceName ?? "").Trim();
        if (source.Length > 0)
        {
            // Depth 1 is the shallowest a completed run can be; 20 is well past the deepest anyone
            // reaches, and the rate table flattens at 9 anyway. 0 clears the override.
            var clamped = averageDepth <= 0 ? 0 : Math.Clamp(averageDepth, 1, 20);
            await repository.Upsert(characterId, source, clamped);
        }
        return await repository.List();
    }

    public async Task<List<CharacterDelveDepthRow>> Remove(int characterId, string sourceName)
    {
        await repository.Upsert(characterId, (sourceName ?? "").Trim(), 0);
        return await repository.List();
    }
}
