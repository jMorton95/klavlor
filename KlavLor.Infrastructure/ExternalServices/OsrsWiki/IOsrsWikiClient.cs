using System.Text.Json.Serialization;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public interface IOsrsWikiClient
{
    Task<List<OsrsSearchResult>> SearchItems(string searchTerm, int limit = 10);

    /// <summary>Fetches the OSRS Wiki's collection-log item list (Module:Collection_log/data.json). Returns empty on failure.</summary>
    Task<IReadOnlyList<CollectionLogItemData>> FetchCollectionLogItems();

    /// <summary>
    /// Queries the wiki's Bucket structured-data store (the `dropsline` bucket) for every drop on
    /// the given source page and returns structured drop-rate rows. Because Bucket is generated
    /// from the rendered page, shared drop-table items (herbs, seeds, gems, rare drop table) are
    /// included with their effective per-source rarity — unlike raw-wikitext parsing, which only
    /// saw the unexpanded table-template calls.
    /// <para>
    /// Returns <c>null</c> when the fetch itself failed (network / HTTP / parse error) so the
    /// caller can retain any existing rows and retry later; returns an empty list when the query
    /// succeeded but the source genuinely has no drops; returns the rows otherwise.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<WikiDropRate>?> FetchDropRatesForSource(string wikiPageTitle);
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
