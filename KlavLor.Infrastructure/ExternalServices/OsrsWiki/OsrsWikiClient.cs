using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public sealed class OsrsWikiClient(HttpClient httpClient, ILogger<OsrsWikiClient> logger) : IOsrsWikiClient
{
    private const string WikiApiBase = "https://oldschool.runescape.wiki/api.php";
    private const string WikiImagesBase = "https://oldschool.runescape.wiki/images/";
    private const string WikiBaseUrl = "https://oldschool.runescape.wiki/w/";

    public async Task<List<OsrsSearchResult>> SearchItems(string searchTerm, int limit = 10)
    {
        try
        {
            var encoded = HttpUtility.UrlEncode(searchTerm);
            var url = $"{WikiApiBase}?action=query&generator=search&gsrsearch={encoded}&gsrnamespace=0&gsrlimit={limit}&prop=pageimages&pithumbsize=50&format=json&formatversion=2";

            var response = await httpClient.GetFromJsonAsync<WikiGeneratorSearchResponse>(url);

            if (response?.Query?.Pages is null)
                return [];

            return response.Query.Pages
                .OrderBy(p => p.Index)
                .Select(page =>
                {
                    var iconUrl = DeriveIconUrl(page.Title, page.PageImage);
                    var wikiUrl = $"{WikiBaseUrl}{page.Title?.Replace(" ", "_")}";
                    return new OsrsSearchResult(page.Title ?? "", iconUrl, wikiUrl);
                })
                .Where(r => !string.IsNullOrEmpty(r.Name))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to search OSRS Wiki for: {SearchTerm}", searchTerm);
            return [];
        }
    }

    private static string? DeriveIconUrl(string? title, string? pageImage)
    {
        if (pageImage is not null)
        {
            // Strip _detail suffix to get the inventory icon filename
            var iconFile = pageImage
                .Replace("_detail_animated.gif", ".png")
                .Replace("_detail.png", ".png");
            return $"{WikiImagesBase}{iconFile}";
        }

        if (title is not null)
        {
            // Fallback: construct from page title
            return $"{WikiImagesBase}{title.Replace(" ", "_")}.png";
        }

        return null;
    }
}

internal sealed class WikiGeneratorSearchResponse
{
    [JsonPropertyName("query")]
    public WikiGeneratorQuery? Query { get; set; }
}

internal sealed class WikiGeneratorQuery
{
    [JsonPropertyName("pages")]
    public List<WikiGeneratorPage>? Pages { get; set; }
}

internal sealed class WikiGeneratorPage
{
    [JsonPropertyName("pageid")]
    public int? PageId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("pageimage")]
    public string? PageImage { get; set; }
}
