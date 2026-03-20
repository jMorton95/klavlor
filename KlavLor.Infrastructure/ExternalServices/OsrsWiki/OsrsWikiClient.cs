using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public sealed class OsrsWikiClient(HttpClient httpClient, ILogger<OsrsWikiClient> logger) : IOsrsWikiClient
{
    private const string WikiApiBase = "https://oldschool.runescape.wiki/api.php";
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

            var results = new List<OsrsSearchResult>();

            foreach (var page in response.Query.Pages.OrderBy(p => p.Index))
            {
                if (string.IsNullOrEmpty(page.Title))
                    continue;

                // Derive the expected inventory icon filename from the page image or title.
                var iconFilename = DeriveIconFilename(page.Title, page.PageImage);

                // Resolve via imageinfo API (follows wiki file redirects, returns CDN URL).
                var iconUrl = iconFilename is not null
                    ? await ResolveImageUrl(iconFilename)
                    : null;

                // Fall back to the search thumbnail if the inventory icon doesn't exist.
                iconUrl ??= page.Thumbnail?.Source;

                var wikiUrl = $"{WikiBaseUrl}{page.Title.Replace(" ", "_")}";
                results.Add(new OsrsSearchResult(page.Title, iconUrl, wikiUrl));
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to search OSRS Wiki for: {SearchTerm}", searchTerm);
            return [];
        }
    }

    /// <summary>
    /// Resolves a wiki filename to its actual CDN URL via the imageinfo API.
    /// Handles file redirects (e.g. "Pharaoh's sceptre.png" → "Pharaoh's sceptre (uncharged).png").
    /// Returns null if the file does not exist.
    /// </summary>
    private async Task<string?> ResolveImageUrl(string filename)
    {
        try
        {
            var encoded = HttpUtility.UrlEncode($"File:{filename}");
            var url = $"{WikiApiBase}?action=query&titles={encoded}&prop=imageinfo&iiprop=url&format=json&formatversion=2";

            var response = await httpClient.GetFromJsonAsync<WikiImageInfoResponse>(url);

            var page = response?.Query?.Pages?.FirstOrDefault();
            if (page is null or { Missing: true })
                return null;

            return page.ImageInfo?.FirstOrDefault()?.Url;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to resolve image URL for {Filename}", filename);
            return null;
        }
    }

    private static string? DeriveIconFilename(string? title, string? pageImage)
    {
        if (pageImage is not null)
        {
            // Strip _detail suffix to get the inventory icon filename
            return pageImage
                .Replace("_detail_animated.gif", ".png")
                .Replace("_detail.png", ".png");
        }

        if (title is not null)
        {
            return $"{title.Replace(" ", "_")}.png";
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

    [JsonPropertyName("thumbnail")]
    public WikiThumbnail? Thumbnail { get; set; }
}

internal sealed class WikiThumbnail
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

// Response models for the imageinfo API
internal sealed class WikiImageInfoResponse
{
    [JsonPropertyName("query")]
    public WikiImageInfoQuery? Query { get; set; }
}

internal sealed class WikiImageInfoQuery
{
    [JsonPropertyName("pages")]
    public List<WikiImageInfoPage>? Pages { get; set; }
}

internal sealed class WikiImageInfoPage
{
    [JsonPropertyName("pageid")]
    public int? PageId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("missing")]
    public bool Missing { get; set; }

    [JsonPropertyName("imageinfo")]
    public List<WikiImageInfo>? ImageInfo { get; set; }
}

internal sealed class WikiImageInfo
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
