using System.Text.Json.Serialization;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public interface IOsrsWikiClient
{
    Task<List<OsrsSearchResult>> SearchItems(string searchTerm, int limit = 10);

    /// <summary>Fetches the OSRS Wiki's collection-log item list (Module:Collection_log/data.json). Returns empty on failure.</summary>
    Task<IReadOnlyList<CollectionLogItemData>> FetchCollectionLogItems();

    /// <summary>
    /// Fetches the wiki page's wikitext and extracts {{DropsLine}} / {{DropsLineClue}}
    /// template invocations into structured drop-rate rows. Returns empty on missing
    /// page, parse failure, or when the page has no DropsLine templates.
    /// </summary>
    Task<IReadOnlyList<WikiDropRate>> FetchDropRatesForSource(string wikiPageTitle);
}

public sealed record OsrsSearchResult(
    string Name,
    string? IconUrl,
    string? WikiUrl
);

/// <summary>One entry from Module:Collection_log/data.json: an item plus the collection-log tabs it appears under.</summary>
public sealed record CollectionLogItemData(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("tabs")] IReadOnlyList<string>? Tabs
);

/// <summary>One DropsLine template invocation parsed out of a source page's wikitext.</summary>
public sealed record WikiDropRate(
    string ItemName,
    string? Quantity,
    string Rarity,
    int? Numerator,
    int? Denominator,
    int Rolls,
    string? Section
);
