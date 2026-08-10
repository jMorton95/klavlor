using KlavLor.Application.Common;
using KlavLor.Application.Features.Drop;

namespace KlavLor.Application.Interfaces.Repositories;

// All-players view of a single dropped item. Every query aggregates over visible characters
// only (matching the public drop-log / feed visibility rules). Sorting for the Sources and
// Characters tables is applied server-side via a whitelisted ORDER BY in the implementation.
public interface IGlobalDropRepository
{
    Task<GlobalDropOverview?> GetOverview(string itemName);
    Task<DropSourceTable> GetSources(string itemName, string sortBy, SortDirection direction, string? term);
    Task<DropCharacterTable> GetCharacters(string itemName, string sortBy, SortDirection direction, string? term);

    /// <summary>
    /// Every source that gave one character this item, most drops first. Null when that character
    /// has never received it (or isn't visible).
    /// </summary>
    Task<DropCharacterSources?> GetCharacterSources(string itemName, int gameCharacterId);
    /// <summary>gameCharacterId scopes to one character; null is the all-players view.</summary>
    Task<List<DropTrendPoint>> GetMonthlyTrend(string itemName, int? gameCharacterId = null);
    Task<List<DropSessionRow>> GetRecentSessions(string itemName, int limit, int? gameCharacterId = null);
}
