namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public interface IOsrsWikiClient
{
    Task<List<OsrsSearchResult>> SearchItems(string searchTerm, int limit = 10);
}

public sealed record OsrsSearchResult(
    string Name,
    string? IconUrl,
    string? WikiUrl
);
