using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using KlavLor.Application.Interfaces.Services;

namespace KlavLor.Infrastructure.Services;

/// <summary>
/// TempleOSRS collection-log client. See ITempleOsrsClient for why this upstream and what the
/// HTTP-200-with-an-error-body trap is.
/// </summary>
internal sealed class TempleOsrsClient(HttpClient http, ILogger<TempleOsrsClient> logger) : ITempleOsrsClient
{
    public const string BaseAddress = "https://templeosrs.com/api/collection-log/";

    // Temple's own error codes, returned inside a 200 body.
    private const int ErrorPlayerNotSynced = 402;
    private const int ErrorPlayerNotFound = 401;

    public async Task<TempleFetchResult<TempleCollectionLog>> GetPlayerCollectionLog(string rsn, CancellationToken ct = default)
    {
        rsn = (rsn ?? "").Trim();
        if (rsn.Length == 0)
            return TempleFetchResult<TempleCollectionLog>.Fail(TempleFetchStatus.NotFound, "No RSN.");

        // categories=all is the whole log; includenames is deliberately omitted — we already hold
        // every item's name locally and asking for them again would roughly double the payload for
        // data we'd throw away. Temple's docs ask callers to request only what they need.
        var url = $"player_collection_log.php?player={Uri.EscapeDataString(rsn)}&categories=all&dateformat=unix";

        var (doc, failure) = await Read<TempleCollectionLog>(url, ct);
        if (failure is not null) return failure;
        using var _ = doc;

        try
        {
            // The player endpoint ALWAYS wraps its payload in "data". Requiring it here — rather
            // than falling back to the root the way the categories endpoint needs — is a safety
            // interlock: an unrecognised body would otherwise parse as a valid log with zero items,
            // and applying that would delete the character's stored collection log.
            if (!doc!.RootElement.TryGetProperty("data", out var d))
                return TempleFetchResult<TempleCollectionLog>.Fail(TempleFetchStatus.Failed, "Response had no data property.");

            var items = new List<TempleCollectionItem>();

            // Items come back grouped by category. We flatten: category membership is static
            // reference data we already store, so re-deriving it per character per hour would be
            // the same rows written N times over.
            if (d.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Object)
            {
                // A category's items arrive as EITHER a JSON array or an object keyed by index,
                // depending on which optional parameters were sent — with dateformat=unix it is an
                // array, with includenames=1 it is an object. Handling only one shape silently
                // yields zero items and a log that reads as "synced, owns nothing".
                foreach (var category in itemsEl.EnumerateObject())
                {
                    foreach (var entry in Entries(category.Value))
                    {
                        if (entry.ValueKind != JsonValueKind.Object) continue;
                        if (!entry.TryGetProperty("id", out var idEl)) continue;
                        var id = ReadInt(idEl) ?? 0;
                        if (id <= 0) continue;

                        var count = entry.TryGetProperty("count", out var cEl) ? ReadInt(cEl) ?? 0 : 0;
                        var obtained = entry.TryGetProperty("date", out var dEl) ? ReadDate(dEl) : null;
                        items.Add(new TempleCollectionItem(id, count, obtained));
                    }
                }
            }

            // An item in several categories arrives several times; keep the richest copy so a date
            // recorded under one category isn't lost to a null under another.
            var deduped = items
                .GroupBy(i => i.ItemId)
                .Select(g => new TempleCollectionItem(
                    g.Key,
                    g.Max(x => x.Count),
                    g.Select(x => x.ObtainedAt).Where(x => x is not null).Min()))
                .ToList();

            return TempleFetchResult<TempleCollectionLog>.Ok(new TempleCollectionLog(
                Rsn: rsn,
                DisplayName: Str(d, "player_name_with_capitalization") ?? Str(d, "player"),
                GameMode: Int(d, "game_mode") ?? 0,
                TotalObtained: Int(d, "total_collections_finished") ?? deduped.Count,
                TotalAvailable: Int(d, "total_collections_available") ?? 0,
                CategoriesFinished: Int(d, "total_categories_finished") ?? 0,
                CategoriesAvailable: Int(d, "total_categories_available") ?? 0,
                HiscoresRank: Int(d, "collections_hiscores_rank"),
                LastChecked: Date(d, "last_checked"),
                LastChanged: Date(d, "last_changed"),
                Items: deduped));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not parse TempleOSRS collection log for {Rsn}", rsn);
            return TempleFetchResult<TempleCollectionLog>.Fail(TempleFetchStatus.Failed, $"Parse error: {ex.Message}");
        }
    }

    public async Task<TempleFetchResult<IReadOnlyDictionary<int, string>>> GetItems(CancellationToken ct = default)
    {
        var (doc, failure) = await Read<IReadOnlyDictionary<int, string>>("items.php", ct);
        if (failure is not null) return failure;
        using var _ = doc;

        try
        {
            var map = new Dictionary<int, string>();
            foreach (var item in Payload(doc!).GetProperty("items").EnumerateObject())
                if (int.TryParse(item.Name, out var id))
                    map[id] = item.Value.GetString() ?? "";

            return TempleFetchResult<IReadOnlyDictionary<int, string>>.Ok(map);
        }
        catch (Exception ex)
        {
            return TempleFetchResult<IReadOnlyDictionary<int, string>>.Fail(TempleFetchStatus.Failed, ex.Message);
        }
    }

    public async Task<TempleFetchResult<IReadOnlyList<TempleCategory>>> GetCategories(CancellationToken ct = default)
    {
        var (doc, failure) = await Read<IReadOnlyList<TempleCategory>>("categories.php", ct);
        if (failure is not null) return failure;
        using var _ = doc;

        try
        {
            // Shape is group -> category slug -> [item ids].
            var categories = new List<TempleCategory>();
            foreach (var group in Payload(doc!).EnumerateObject())
            {
                if (group.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var category in group.Value.EnumerateObject())
                {
                    if (category.Value.ValueKind != JsonValueKind.Array) continue;
                    var ids = category.Value.EnumerateArray().Select(ReadInt).Where(i => i is > 0).Select(i => i!.Value).ToList();
                    categories.Add(new TempleCategory(category.Name, group.Name, ids));
                }
            }

            return TempleFetchResult<IReadOnlyList<TempleCategory>>.Ok(categories);
        }
        catch (Exception ex)
        {
            return TempleFetchResult<IReadOnlyList<TempleCategory>>.Fail(TempleFetchStatus.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Fetch and pre-flight one response: transport failure, non-200, unparseable body, or Temple's
    /// in-body error envelope. Only a body that clears all four reaches a caller.
    /// </summary>
    private async Task<(JsonDocument? Doc, TempleFetchResult<T>? Failure)> Read<T>(string url, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "TempleOSRS request failed: {Url}", url);
            return (null, TempleFetchResult<T>.Fail(TempleFetchStatus.Failed, ex.Message));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return (null, TempleFetchResult<T>.Fail(TempleFetchStatus.Failed, $"HTTP {(int)response.StatusCode}"));

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            }
            catch (JsonException ex)
            {
                return (null, TempleFetchResult<T>.Fail(TempleFetchStatus.Failed, $"Malformed JSON: {ex.Message}"));
            }

            // THE TRAP. A 200 whose body is an error envelope. Treating this as success would store
            // an empty log over a good one.
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("Code", out var c) ? ReadInt(c) : null;
                var message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("Message", out var m)
                    ? m.GetString()
                    : error.ToString();

                doc.Dispose();
                var status = code switch
                {
                    ErrorPlayerNotSynced => TempleFetchStatus.NotSynced,
                    ErrorPlayerNotFound => TempleFetchStatus.NotFound,
                    // An unrecognised code is still a definite refusal, not a transport blip; treat
                    // it as "no data" rather than retrying it as if it might succeed next hour.
                    _ => TempleFetchStatus.NotSynced
                };
                return (null, TempleFetchResult<T>.Fail(status, message));
            }

            // Not every endpoint wraps its payload: player and items responses use a "data"
            // envelope, categories returns its five groups at the root. Callers read through
            // Payload() rather than assuming either shape.
            return (doc, null);
        }
    }

    /// <summary>A category's item entries, whichever of the two shapes Temple used.</summary>
    private static IEnumerable<JsonElement> Entries(JsonElement category) => category.ValueKind switch
    {
        JsonValueKind.Array => category.EnumerateArray(),
        JsonValueKind.Object => category.EnumerateObject().Select(p => p.Value),
        _ => []
    };

    /// <summary>The payload element: the "data" envelope when present, else the root.</summary>
    private static JsonElement Payload(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;

    // --- JSON helpers. Temple is loosely typed: numbers arrive as numbers or strings depending on
    // the field and the endpoint, so every read tolerates both rather than throwing.

    private static int? ReadInt(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number when el.TryGetInt32(out var n) => n,
        JsonValueKind.String when int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
        _ => null
    };

    private static int? Int(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var el) ? ReadInt(el) : null;

    private static string? Str(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static DateTimeOffset? Date(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var el) ? ReadDate(el) : null;

    /// <summary>
    /// Dates arrive either as a unix timestamp (we ask for dateformat=unix) or as
    /// "yyyy-MM-dd HH:mm:ss" in UTC. A zero timestamp means "unknown", not 1970.
    /// </summary>
    private static DateTimeOffset? ReadDate(JsonElement el)
    {
        if (ReadInt(el) is { } unix)
            return unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix) : null;

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (long.TryParse(s, out var asUnix)) return asUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(asUnix) : null;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        return null;
    }
}
