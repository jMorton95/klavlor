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
    Task<List<DropTrendPoint>> GetMonthlyTrend(string itemName);
    Task<List<DropSessionRow>> GetRecentSessions(string itemName, int limit);
}
