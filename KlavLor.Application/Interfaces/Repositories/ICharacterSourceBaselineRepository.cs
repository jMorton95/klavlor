using KlavLor.Application.Features.Loot.Baseline;

namespace KlavLor.Application.Interfaces.Repositories;

public interface ICharacterSourceBaselineRepository
{
    // Baseline seed for a character at a source, or 0 if none is set.
    Task<int> GetBaseline(int characterId, string sourceName);

    // Set (or clear, when baselineKc <= 0) the seed for a character/source.
    Task Upsert(int characterId, string sourceName, int baselineKc);

    // Current baselines with character display names, for the admin list.
    Task<List<CharacterBaselineRow>> List();
}
