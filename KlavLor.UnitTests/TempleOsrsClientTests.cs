using System.Net;
using System.Text;
using KlavLor.Application.Interfaces.Services;
using KlavLor.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.UnitTests;

// TempleOSRS returns errors as HTTP 200 with an error envelope in the body. Anything that trusts
// the status code reports a permanent failure as a success — and because a "successful" empty log
// would be applied over the stored one, that mistake deletes a character's collection log.
//
// These tests exist for that one hazard, plus the loose typing around it: Temple returns numbers as
// numbers or strings depending on the field, and dates as unix seconds or "yyyy-MM-dd HH:mm:ss".
public sealed class TempleOsrsClientTests
{
    private static TempleOsrsClient Client(HttpStatusCode status, string body)
    {
        var http = new HttpClient(new StubHandler(status, body))
        {
            BaseAddress = new Uri(TempleOsrsClient.BaseAddress)
        };
        return new TempleOsrsClient(http, NullLogger<TempleOsrsClient>.Instance);
    }

    [Fact]
    public async Task A_200_carrying_an_error_envelope_is_not_a_success()
    {
        // The exact body Temple returns for a player who has never pressed sync.
        var client = Client(HttpStatusCode.OK,
            """{"error":{"Code":402,"Message":"Player has not synced their collection log on TempleOSRS yet."}}""");

        var result = await client.GetPlayerCollectionLog("Someone");

        Assert.False(result.IsOk);
        Assert.Equal(TempleFetchStatus.NotSynced, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("not synced", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_player_is_distinguished_from_an_unsynced_one()
    {
        var client = Client(HttpStatusCode.OK, """{"error":{"Code":401,"Message":"Player not found."}}""");

        var result = await client.GetPlayerCollectionLog("Nobody");

        // The two lead to different messages on screen and different retry behaviour, so they must
        // not collapse into one status.
        Assert.Equal(TempleFetchStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task An_unrecognised_error_code_is_still_treated_as_no_data()
    {
        var client = Client(HttpStatusCode.OK, """{"error":{"Code":999,"Message":"Something new."}}""");

        var result = await client.GetPlayerCollectionLog("Someone");

        Assert.False(result.IsOk);
        Assert.NotEqual(TempleFetchStatus.Ok, result.Status);
    }

    [Fact]
    public async Task A_non_200_is_a_transport_failure_not_a_missing_player()
    {
        var client = Client(HttpStatusCode.ServiceUnavailable, "upstream down");

        var result = await client.GetPlayerCollectionLog("Someone");

        // Failed retries; NotSynced/NotFound back off. Confusing them would either hammer a broken
        // upstream or give up on a player over one bad minute.
        Assert.Equal(TempleFetchStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Malformed_json_is_a_failure_rather_than_an_exception()
    {
        var client = Client(HttpStatusCode.OK, "<html>not json</html>");

        var result = await client.GetPlayerCollectionLog("Someone");

        Assert.Equal(TempleFetchStatus.Failed, result.Status);
    }

    [Fact]
    public async Task A_body_with_no_data_property_is_a_failure()
    {
        var client = Client(HttpStatusCode.OK, """{"something":"else"}""");

        Assert.Equal(TempleFetchStatus.Failed, (await client.GetPlayerCollectionLog("Someone")).Status);
    }

    [Fact]
    public async Task A_real_shaped_response_is_parsed_with_its_totals_and_dates()
    {
        var client = Client(HttpStatusCode.OK,
            """
            {"data":{
              "player":"klavelon",
              "player_name_with_capitalization":"Klavelon",
              "game_mode":1,
              "last_checked":"2026-08-10 09:29:24",
              "last_changed":"2026-08-09 19:09:21",
              "total_collections_finished":571,
              "total_collections_available":1712,
              "total_categories_finished":12,
              "total_categories_available":124,
              "collections_hiscores_rank":33695,
              "items":{
                "abyssal_sire":{"0":{"id":4151,"count":1,"date":"2026-06-11 13:56:16","name":"Abyssal whip"},
                                "1":{"id":13262,"count":"3","date":0,"name":"Abyssal dagger"}}
              }}}
            """);

        var result = await client.GetPlayerCollectionLog("Klavelon");

        Assert.True(result.IsOk);
        var log = result.Value!;
        Assert.Equal("Klavelon", log.DisplayName);
        Assert.Equal(1, log.GameMode);
        Assert.Equal(1712, log.TotalAvailable);
        Assert.Equal(33695, log.HiscoresRank);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 19, 9, 21, TimeSpan.Zero), log.LastChanged);

        Assert.Equal(2, log.Items.Count);
        var whip = log.Items.Single(i => i.ItemId == 4151);
        Assert.Equal(1, whip.Count);
        Assert.Equal(new DateTimeOffset(2026, 6, 11, 13, 56, 16, TimeSpan.Zero), whip.ObtainedAt);

        // A count arriving as a string still parses, and a zero date means "unknown", not 1970 —
        // storing the epoch would render as an obtained date of January 1970 on every such item.
        var dagger = log.Items.Single(i => i.ItemId == 13262);
        Assert.Equal(3, dagger.Count);
        Assert.Null(dagger.ObtainedAt);
    }

    [Fact]
    public async Task Items_parse_whether_a_category_is_an_array_or_an_index_keyed_object()
    {
        // Temple switches shape on the optional parameters: dateformat=unix returns arrays,
        // includenames=1 returns objects keyed by index. Handling only one shape parsed zero items
        // while every header field still read correctly — a log that looked synced but empty.
        const string asArray = """{"data":{"player":"x","items":{"abyssal_sire":[{"id":4151,"count":1,"date":1781186176}]}}}""";
        const string asObject = """{"data":{"player":"x","items":{"abyssal_sire":{"0":{"id":4151,"count":1,"date":1781186176}}}}}""";

        foreach (var body in new[] { asArray, asObject })
        {
            var result = await Client(HttpStatusCode.OK, body).GetPlayerCollectionLog("x");

            Assert.True(result.IsOk);
            var item = Assert.Single(result.Value!.Items);
            Assert.Equal(4151, item.ItemId);
            Assert.Equal(1, item.Count);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1781186176), item.ObtainedAt);
        }
    }

    [Fact]
    public async Task An_item_listed_under_several_categories_is_returned_once_keeping_its_date()
    {
        // Shared drops (rare-drop-table items, clue rewards) appear under more than one category,
        // sometimes with a date under one and none under another.
        var client = Client(HttpStatusCode.OK,
            """
            {"data":{"player":"x","items":{
               "slayer":{"0":{"id":4151,"count":1,"date":"2026-06-11 13:56:16"}},
               "misc":{"0":{"id":4151,"count":1,"date":0}}
            }}}
            """);

        var result = await client.GetPlayerCollectionLog("x");

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(4151, item.ItemId);
        Assert.Equal(new DateTimeOffset(2026, 6, 11, 13, 56, 16, TimeSpan.Zero), item.ObtainedAt);
    }

    [Fact]
    public async Task Categories_are_flattened_from_their_groups()
    {
        var client = Client(HttpStatusCode.OK,
            """{"data":{"bosses":{"abyssal_sire":[4151,13262]},"raids":{"chambers_of_xeric":[20997]}}}""");

        var result = await client.GetCategories();

        Assert.True(result.IsOk);
        Assert.Equal(2, result.Value!.Count);
        var sire = result.Value.Single(c => c.Slug == "abyssal_sire");
        Assert.Equal("bosses", sire.GroupName);
        Assert.Equal([4151, 13262], sire.ItemIds);
    }

    [Fact]
    public async Task An_empty_rsn_never_reaches_the_upstream()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new TempleOsrsClient(
            new HttpClient(handler) { BaseAddress = new Uri(TempleOsrsClient.BaseAddress) },
            NullLogger<TempleOsrsClient>.Instance);

        var result = await client.GetPlayerCollectionLog("   ");

        Assert.Equal(TempleFetchStatus.NotFound, result.Status);
        Assert.Equal(0, handler.Calls);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
