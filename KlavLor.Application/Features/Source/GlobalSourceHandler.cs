using KlavLor.Application.Interfaces.Repositories;

namespace KlavLor.Application.Features.Source;

public sealed class GlobalSourceHandler(IGlobalSourceRepository repository)
{
    public const int TopDropsLimit = 12;
    public const int PlayersLimit = 12;
    public const int DropSearchLimit = 50;

    public Task<GlobalSourceOverview?> GetOverview(string sourceName) =>
        repository.GetOverview(sourceName);

    public Task<List<GlobalSourceDrop>> GetTopDrops(string sourceName) =>
        repository.GetTopDrops(sourceName, TopDropsLimit);

    public Task<List<SourcePlayerRow>> GetPlayers(string sourceName) =>
        repository.GetPlayers(sourceName, PlayersLimit);

    public Task<List<GlobalSourceDrop>> SearchDrops(string sourceName, string? term) =>
        repository.SearchDrops(sourceName, term, DropSearchLimit);

    public Task<GlobalSourceCoverage> GetCollectionCoverage(string sourceName) =>
        repository.GetCollectionCoverage(sourceName);
}
