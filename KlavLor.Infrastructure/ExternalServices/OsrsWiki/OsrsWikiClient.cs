using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.Logging;

namespace KlavLor.Infrastructure.ExternalServices.OsrsWiki;

public sealed class OsrsWikiClient(HttpClient httpClient, ILogger<OsrsWikiClient> logger) : IOsrsWikiClient
{
    private const string WikiApiBase = "https://oldschool.runescape.wiki/api.php";
    private const string WikiBaseUrl = "https://oldschool.runescape.wiki/w/";
    private const string CollectionLogDataUrl = "https://oldschool.runescape.wiki/?title=Module:Collection_log/data.json&action=raw&ctype=application/json";

    public async Task<IReadOnlyList<CollectionLogItemData>> FetchCollectionLogItems()
    {
        try
        {
            var items = await httpClient.GetFromJsonAsync<List<CollectionLogItemData>>(CollectionLogDataUrl);
            return items ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch OSRS collection log data");
            return [];
        }
    }

    public async Task<IReadOnlyList<WikiDropRate>> FetchDropRatesForSource(string wikiPageTitle)
    {
        if (string.IsNullOrWhiteSpace(wikiPageTitle)) return [];

        try
        {
            var encoded = HttpUtility.UrlEncode(wikiPageTitle);
            var url = $"{WikiApiBase}?action=parse&page={encoded}&prop=wikitext&format=json&formatversion=2";

            var response = await httpClient.GetFromJsonAsync<WikiParseResponse>(url);
            var wikitext = response?.Parse?.Wikitext;
            if (string.IsNullOrWhiteSpace(wikitext)) return [];

            return ParseDropsLines(wikitext);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch drop rates for {Page}", wikiPageTitle);
            return [];
        }
    }

    // Walks the wikitext sequentially, tracking the closest preceding ==Section==
    // heading and emitting each {{DropsLine|...}} / {{DropsLineClue|...}} body it
    // finds. Honours nested {{...}} so a rarity={{Brimstone rarity|725}} parameter
    // doesn't terminate the outer template early.
    private static List<WikiDropRate> ParseDropsLines(string wikitext)
    {
        var results = new List<WikiDropRate>();
        string? section = null;

        int i = 0;
        while (i < wikitext.Length)
        {
            // Section heading on a fresh line: ==Heading==, ===Sub===, etc.
            if (i == 0 || wikitext[i - 1] == '\n')
            {
                var headingMatch = HeadingRegex.Match(wikitext, i);
                if (headingMatch.Success && headingMatch.Index == i)
                {
                    section = headingMatch.Groups[2].Value.Trim();
                    i = headingMatch.Index + headingMatch.Length;
                    continue;
                }
            }

            // {{DropsLine| or {{DropsLineClue|
            var prefix = MatchDropsLinePrefix(wikitext, i);
            if (prefix is { } p)
            {
                var (afterPrefix, _) = p;
                var endIdx = FindMatchingClose(wikitext, afterPrefix);
                if (endIdx < 0)
                {
                    // Unbalanced template — bail; the source page is malformed and
                    // we'd rather miss it than mis-parse downstream entries.
                    break;
                }

                var body = wikitext.Substring(afterPrefix, endIdx - afterPrefix);
                var rate = ParseDropsLineBody(body, section);
                if (rate is not null) results.Add(rate);
                i = endIdx + 2;
                continue;
            }

            i++;
        }

        return results;
    }

    private static (int AfterPrefix, bool IsClue)? MatchDropsLinePrefix(string text, int i)
    {
        // Variants share the {{DropsLine prefix; check the longer ones first so
        // {{DropsLineClue / {{DropsLineReward aren't accidentally treated as {{DropsLine.
        // DropsLineReward is used on raid / minigame reward chests (e.g. The Gauntlet,
        // Tombs of Amascut) and carries the same name/quantity/rarity parameters.
        if (StartsWith(text, i, "{{DropsLineClue|")) return (i + "{{DropsLineClue|".Length, true);
        if (StartsWith(text, i, "{{DropsLineReward|")) return (i + "{{DropsLineReward|".Length, false);
        if (StartsWith(text, i, "{{DropsLine|")) return (i + "{{DropsLine|".Length, false);
        return null;
    }

    private static bool StartsWith(string text, int i, string needle)
    {
        if (i + needle.Length > text.Length) return false;
        return text.AsSpan(i, needle.Length).SequenceEqual(needle.AsSpan());
    }

    // Returns the index of the }} that closes the template starting at i (where i
    // is *inside* the template body). Returns -1 if unbalanced.
    private static int FindMatchingClose(string text, int i)
    {
        int depth = 1;
        while (i < text.Length - 1)
        {
            if (text[i] == '{' && text[i + 1] == '{') { depth++; i += 2; }
            else if (text[i] == '}' && text[i + 1] == '}')
            {
                depth--;
                if (depth == 0) return i;
                i += 2;
            }
            else i++;
        }
        return -1;
    }

    private static WikiDropRate? ParseDropsLineBody(string body, string? section)
    {
        string? name = null;
        string? quantity = null;
        string? rarity = null;
        var rolls = 1;

        foreach (var param in SplitTopLevelPipes(body))
        {
            var eq = param.IndexOf('=');
            if (eq <= 0) continue;
            var key = param[..eq].Trim().ToLowerInvariant();
            var value = param[(eq + 1)..].Trim();
            switch (key)
            {
                case "name": name = value; break;
                case "quantity": quantity = value; break;
                case "rarity": rarity = StripRefs(value); break;
                case "rolls":
                    if (int.TryParse(value, out var r)) rolls = r;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rarity))
            return null;

        var (num, den) = ParseNumericRarity(rarity);
        return new WikiDropRate(name, quantity, rarity, num, den, rolls, section);
    }

    private static IEnumerable<string> SplitTopLevelPipes(string body)
    {
        int depth = 0;
        int start = 0;
        for (int i = 0; i < body.Length; i++)
        {
            if (i + 1 < body.Length && body[i] == '{' && body[i + 1] == '{') { depth++; i++; continue; }
            if (i + 1 < body.Length && body[i] == '}' && body[i + 1] == '}') { depth--; i++; continue; }
            if (depth == 0 && body[i] == '|')
            {
                yield return body[start..i];
                start = i + 1;
            }
        }
        if (start < body.Length) yield return body[start..];
    }

    private static (int? Numerator, int? Denominator) ParseNumericRarity(string rarity)
    {
        var match = NumericRarityRegex.Match(rarity);
        if (!match.Success) return (null, null);
        if (!int.TryParse(match.Groups[1].Value, out var num)) return (null, null);
        if (!int.TryParse(match.Groups[2].Value, out var den) || den <= 0) return (null, null);
        return (num, den);
    }

    private static string StripRefs(string value) =>
        RefTagRegex.Replace(value, "").Trim();

    private static readonly Regex HeadingRegex = new(@"^(={2,})\s*(.+?)\s*\1\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex NumericRarityRegex = new(@"^\s*(\d+)\s*/\s*(\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex RefTagRegex = new(@"<ref[^>]*?(?:/>|>.*?</ref>)", RegexOptions.Compiled | RegexOptions.Singleline);

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

internal sealed class WikiParseResponse
{
    [JsonPropertyName("parse")]
    public WikiParseResult? Parse { get; set; }
}

internal sealed class WikiParseResult
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("wikitext")]
    public string? Wikitext { get; set; }
}
