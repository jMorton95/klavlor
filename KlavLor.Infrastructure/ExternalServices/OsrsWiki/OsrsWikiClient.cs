using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
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

    public async Task<IReadOnlyList<WikiDropRate>?> FetchDropRatesForSource(string wikiPageTitle)
    {
        if (string.IsNullOrWhiteSpace(wikiPageTitle)) return [];

        try
        {
            // Query the wiki's Bucket structured-data store (Weird Gloop's replacement for
            // SMW/Cargo) rather than scraping raw wikitext. The `dropsline` bucket is generated
            // from the *rendered* page, so items pulled in from shared drop tables (herb / seed /
            // gem / rare-drop-table) appear as ordinary rows on the source with their effective
            // per-source rarity already computed — exactly the multi-source items the old
            // wikitext scraper missed (it only saw the unexpanded {{HerbDropLines}}-style calls).
            //
            // Double-quoted Lua string literal for the page name so sources with apostrophes
            // (K'ril Tsutsaroth, Kree'arra, …) don't need quote-escaping. limit is a defensive
            // cap; the biggest single source (a raid boss) has well under 200 rows.
            var query = $"bucket('dropsline').select('drop_json').where('page_name',\"{EscapeBucketString(wikiPageTitle)}\").limit(1000).run()";
            var url = $"{WikiApiBase}?action=bucket&format=json&query={HttpUtility.UrlEncode(query)}";

            var response = await httpClient.GetFromJsonAsync<BucketResponse>(url);
            // A missing `bucket` array means an error/unexpected envelope, not a genuine
            // no-drops result (that comes back as an empty array) — signal failure with null.
            if (response?.Bucket is not { } rows) return null;

            var results = new List<WikiDropRate>(rows.Count);
            foreach (var row in rows)
            {
                if (string.IsNullOrEmpty(row.DropJson)) continue;
                var rate = ParseDropJson(row.DropJson);
                if (rate is not null) results.Add(rate);
            }
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch drop rates for {Page}", wikiPageTitle);
            return null;
        }
    }

    // Each dropsline row carries a `drop_json` string holding the full drop record. We only need
    // the item, its rarity/quantity/rolls, and the variant anchor for section filtering.
    private static WikiDropRate? ParseDropJson(string dropJson)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(dropJson); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var name = GetJsonString(root, "Dropped item");
            if (string.IsNullOrWhiteSpace(name)) return null;

            var rarity = GetJsonString(root, "Rarity") ?? "";
            var quantity = GetJsonString(root, "Drop Quantity");
            var rolls = GetJsonInt(root, "Rolls") ?? 1;

            // Variant sources share one wiki page and encode the variant as an anchor on
            // "Dropped from" (e.g. "The Gauntlet#Corrupted"). Surface that as the Section so
            // DropRateSyncRunner's SectionFilter keeps disambiguating them unchanged.
            var section = ExtractAnchor(GetJsonString(root, "Dropped from"));

            var (num, den) = ParseNumericRarity(rarity);
            return new WikiDropRate(name, quantity, rarity, num, den, rolls, section);
        }
    }

    private static string? GetJsonString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetJsonInt(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static string? ExtractAnchor(string? droppedFrom)
    {
        if (string.IsNullOrEmpty(droppedFrom)) return null;
        var hash = droppedFrom.IndexOf('#');
        return hash >= 0 && hash < droppedFrom.Length - 1 ? droppedFrom[(hash + 1)..].Trim() : null;
    }

    // Bucket string values are wrapped in a double-quoted Lua literal; escape backslashes and
    // double quotes so an odd page name can't break out of the literal.
    private static string EscapeBucketString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // Reduces a wiki rarity string to an integer N/D pair for the luck math. Handles the formats
    // the Bucket data uses that the old {{DropsLine}} parser never saw: thousands separators
    // ("1/47,826") and — critically for shared-table items — decimal denominators ("1/32.4",
    // "1/268.75"). Decimals are cleared by scaling both parts by a power of ten, which preserves
    // the exact ratio (every consumer uses N/D only as a double ratio, never the denominator
    // alone). Non-numeric rarities ("Always", "Varies") return (null, null) and display verbatim.
    private static (int? Numerator, int? Denominator) ParseNumericRarity(string rarity)
    {
        if (string.IsNullOrWhiteSpace(rarity)) return (null, null);

        var cleaned = rarity.Replace(",", "").Replace("~", "").Trim();
        var match = RarityRegex.Match(cleaned);
        if (!match.Success) return (null, null);
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) || num <= 0)
            return (null, null);

        var denStr = match.Groups[2].Value;
        if (!denStr.Contains('.'))
        {
            return int.TryParse(denStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var den) && den > 0
                ? (num, den)
                : (null, null);
        }

        if (!double.TryParse(denStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var denVal) || denVal <= 0)
            return (null, null);

        var decimals = denStr.Length - denStr.IndexOf('.') - 1;
        var factor = (long)Math.Pow(10, decimals);
        var scaledNum = (long)num * factor;
        var scaledDen = (long)Math.Round(denVal * factor);
        if (scaledNum is <= 0 or > int.MaxValue || scaledDen is <= 0 or > int.MaxValue)
        {
            // Too large to scale exactly — fall back to a rounded integer denominator.
            var rounded = (int)Math.Round(denVal);
            return rounded > 0 ? (num, rounded) : (null, null);
        }

        return ((int)scaledNum, (int)scaledDen);
    }

    private static readonly Regex RarityRegex = new(@"^(\d+)\s*/\s*(\d+(?:\.\d+)?)$", RegexOptions.Compiled);

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

// Response envelope for action=bucket: { "bucketQuery": ..., "bucket": [ { <selected fields> } ] }.
internal sealed class BucketResponse
{
    [JsonPropertyName("bucket")]
    public List<BucketRow>? Bucket { get; set; }
}

internal sealed class BucketRow
{
    [JsonPropertyName("drop_json")]
    public string? DropJson { get; set; }
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
