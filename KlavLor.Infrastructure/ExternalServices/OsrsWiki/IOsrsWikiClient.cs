using System.Text.Json.Serialization;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public interface IOsrsWikiClient
{
    Task<List<OsrsSearchResult>> SearchItems(string searchTerm, int limit = 10);

    /// <summary>Fetches the OSRS Wiki's collection-log item list (Module:Collection_log/data.json). Returns empty on failure.</summary>
    Task<IReadOnlyList<CollectionLogItemData>> FetchCollectionLogItems();
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
