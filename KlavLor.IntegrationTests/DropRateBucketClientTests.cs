using System.Net;
using System.Text;
using System.Text.Json;
using KlavLor.Infrastructure.ExternalServices.OsrsWiki;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// Offline tests for the Bucket-based drop-rate fetch: a stub HttpMessageHandler feeds canned
// action=bucket responses, so the real parsing path is exercised with no network dependency.
public sealed class DropRateBucketClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private static OsrsWikiClient ClientReturning(HttpStatusCode status, string body)
        => new(new HttpClient(new StubHandler(status, body)), NullLogger<OsrsWikiClient>.Instance);

    // Builds a { "bucket": [ { "drop_json": "<escaped json>" }, ... ] } envelope from drop records,
    // matching the real action=bucket shape (drop_json is a nested JSON *string*).
    private static string BucketBody(params (string Item, string Rarity, string Qty, int Rolls, string From)[] drops)
    {
        var rows = drops.Select(d =>
        {
            var inner = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["Dropped item"] = d.Item,
                ["Rarity"] = d.Rarity,
                ["Drop Quantity"] = d.Qty,
                ["Rolls"] = d.Rolls,
                ["Dropped from"] = d.From
            });
            return new { drop_json = inner };
        }).ToArray();
        return JsonSerializer.Serialize(new { bucket = rows });
    }

    [Fact]
    public async Task Parses_all_rarity_forms_including_previously_unsupported_ones()
    {
        var body = BucketBody(
            ("Grimy ranarr weed", "1/32.4", "1-2", 1, "Chaos druid"),          // decimal denominator (herb table)
            ("Loop half of key", "1/16,384", "1", 1, "Chaos druid"),           // thousands separator
            ("Bones", "Always", "1", 1, "Chaos druid"),                        // non-numeric
            ("Coins", "36.7/127", "1", 1, "Chaos druid"),                      // decimal numerator (unsupported -> raw)
            ("Enhanced crystal weapon seed", "1/400", "1", 1, "Reward Chest (The Gauntlet)#Corrupted")); // variant anchor

        var result = await ClientReturning(HttpStatusCode.OK, body).FetchDropRatesForSource("Chaos druid");

        Assert.NotNull(result);
        var byName = result!.ToDictionary(r => r.ItemName);

        // Decimal denominator scaled to an equivalent integer ratio (the multi-source case that
        // the old raw-wikitext scraper could never produce).
        Assert.Equal("1/32.4", byName["Grimy ranarr weed"].Rarity);
        Assert.Equal(10, byName["Grimy ranarr weed"].Numerator);
        Assert.Equal(324, byName["Grimy ranarr weed"].Denominator);

        // Thousands separator stripped.
        Assert.Equal(1, byName["Loop half of key"].Numerator);
        Assert.Equal(16384, byName["Loop half of key"].Denominator);

        // Non-numeric and decimal-numerator rarities keep their raw string but yield no N/D.
        Assert.Null(byName["Bones"].Numerator);
        Assert.Equal("Always", byName["Bones"].Rarity);
        Assert.Null(byName["Coins"].Numerator);
        Assert.Equal("36.7/127", byName["Coins"].Rarity);

        // Variant anchor on "Dropped from" surfaces as Section for the runner's SectionFilter.
        Assert.Equal("Corrupted", byName["Enhanced crystal weapon seed"].Section);
        Assert.Equal(400, byName["Enhanced crystal weapon seed"].Denominator);
    }

    [Fact]
    public async Task Returns_null_on_fetch_error_and_empty_on_no_drops()
    {
        // HTTP error -> null (fetch failed; caller should keep existing rows).
        Assert.Null(await ClientReturning(HttpStatusCode.InternalServerError, "").FetchDropRatesForSource("X"));

        // Successful query with an empty bucket -> empty list (genuinely no drops).
        var empty = await ClientReturning(HttpStatusCode.OK, "{\"bucket\":[]}").FetchDropRatesForSource("X");
        Assert.NotNull(empty);
        Assert.Empty(empty!);
    }
}
