using KlavLor.Application.Features.Source;

namespace KlavLor.Application.Interfaces.Repositories;

// All-players view of a single loot source. Every query aggregates over visible
// characters only (matching the public drop-log / feed visibility rules).
public interface IGlobalSourceRepository
{
    Task<GlobalSourceOverview?> GetOverview(string sourceName);
    Task<List<GlobalSourceDrop>> GetTopDrops(string sourceName, int limit);
    Task<List<SourcePlayerRow>> GetPlayers(string sourceName, int limit);
    Task<List<SourceClogEvent>> GetRecentClogs(string sourceName, int limit);
    Task<List<SourceItemFrequency>> GetItemFrequency(string sourceName, string? term, int limit);
    Task<List<SourceTrendPoint>> GetMonthlyTrend(string sourceName);
}
