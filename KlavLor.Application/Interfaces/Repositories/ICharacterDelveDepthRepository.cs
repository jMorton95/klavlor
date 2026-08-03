using KlavLor.Application.Features.Loot.DelveDepth;

namespace KlavLor.Application.Interfaces.Repositories;

public interface ICharacterDelveDepthRepository
{
    // Admin-set average delve depth for a character at a source, or null when unset (use the default).
    Task<int?> GetAverageDepth(int characterId, string sourceName);

    // Set (or clear, when averageDepth <= 0) the override for a character/source.
    Task Upsert(int characterId, string sourceName, int averageDepth);

    // Current overrides with character display names, for the admin list.
    Task<List<CharacterDelveDepthRow>> List();
}
