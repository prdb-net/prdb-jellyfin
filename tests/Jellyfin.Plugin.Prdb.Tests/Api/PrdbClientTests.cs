using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Prdb.Api;

namespace Jellyfin.Plugin.Prdb.Tests.Api;

public class PrdbClientTests
{
    private static readonly PrdbConnection Connection = new(new Uri("https://api.example.test"), "secret-key");

    private readonly FakeHandler _handler = new();
    private readonly List<TimeSpan> _waits = [];

    private PrdbClient Client => new(new FakeHttpClientFactory(_handler), (wait, _) =>
    {
        _waits.Add(wait);
        return Task.CompletedTask;
    });

    [Fact]
    public async Task Identify_sends_the_files_with_the_key_and_reads_numeric_enums()
    {
        _handler.Respond(HttpStatusCode.OK, """
            {"results":[
              {"ref":"a","confidence":4,"matchedBy":0,"videoId":"11111111-1111-1111-1111-111111111111","candidates":[],
               "video":{"id":"11111111-1111-1111-1111-111111111111","title":"A video","releaseDate":"2024-05-06",
                        "site":{"id":"22222222-2222-2222-2222-222222222222","title":"Site","url":"https://site.example"},
                        "images":[],"actors":[]}},
              {"ref":"b","confidence":0,"candidates":[]}
            ]}
            """);

        var results = await Client.IdentifyAsync(
            Connection,
            [new IdentifyFile("a", "a.mp4", 123, "0123456789abcdef"), new IdentifyFile("b", "b.mp4", null, null)],
            includeVideoDetails: true,
            CancellationToken.None);

        var (request, body) = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.example.test/videos/identify", request.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
        Assert.StartsWith("Jellyfin.Plugin.Prdb/", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(body!), request.Content!.Headers.ContentLength);
        Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);

        using var json = JsonDocument.Parse(body!);
        Assert.True(json.RootElement.GetProperty("includeVideoDetails").GetBoolean());
        var files = json.RootElement.GetProperty("files");
        Assert.Equal("0123456789abcdef", files[0].GetProperty("osHash").GetString());
        Assert.Equal(123, files[0].GetProperty("filesize").GetInt64());
        Assert.False(files[1].TryGetProperty("osHash", out _), "a missing hash is left out rather than sent as null");

        Assert.Equal(IdentifyConfidence.Exact, results[0].Confidence);
        Assert.Equal(IdentifyMatchKind.OsHash, results[0].MatchedBy);
        Assert.Equal(new DateOnly(2024, 5, 6), results[0].Video!.ReleaseDate);
        Assert.Equal("Site", results[0].Video!.Site!.Title);
        Assert.Equal(IdentifyConfidence.None, results[1].Confidence);
        Assert.Null(results[1].MatchedBy);
    }

    [Fact]
    public async Task An_unknown_enum_value_from_a_newer_api_does_not_fail()
    {
        _handler.Respond(HttpStatusCode.OK, """{"results":[{"ref":"a","confidence":9,"matchedBy":42,"candidates":[]}]}""");

        var results = await Client.IdentifyAsync(Connection, [new IdentifyFile("a", "a.mp4", null, null)], false, CancellationToken.None);

        Assert.Equal((IdentifyConfidence)9, results[0].Confidence);
        Assert.Equal((IdentifyMatchKind)42, results[0].MatchedBy);
    }

    [Fact]
    public async Task Identify_splits_large_inputs_into_batches_of_two_hundred()
    {
        var files = Enumerable.Range(0, 450).Select(i => new IdentifyFile("f" + i, i + ".mp4", null, null)).ToList();
        for (var batch = 0; batch < 3; batch++)
        {
            _handler.RespondWith((_, body) =>
            {
                using var json = JsonDocument.Parse(body!);
                var refs = json.RootElement.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("ref").GetString());
                var results = string.Join(",", refs.Select(r => $$"""{"ref":"{{r}}","confidence":0,"candidates":[]}"""));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"results\":[" + results + "]}", Encoding.UTF8, "application/json"),
                };
            });
        }

        var results = await Client.IdentifyAsync(Connection, files, false, CancellationToken.None);

        Assert.Equal([200, 200, 50], _handler.Requests.Select(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("files").GetArrayLength()));
        Assert.Equal(files.Select(f => f.Ref), results.Select(r => r.Ref));
    }

    [Fact]
    public async Task A_result_list_that_does_not_match_the_batch_is_an_error()
    {
        _handler.Respond(HttpStatusCode.OK, """{"results":[]}""");

        await Assert.ThrowsAsync<PrdbApiException>(
            () => Client.IdentifyAsync(Connection, [new IdentifyFile("a", "a.mp4", null, null)], false, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_rejected_key_fails_at_once_without_retrying(HttpStatusCode status)
    {
        _handler.Respond(status, """{"title":"Unauthorized"}""");

        var ex = await Assert.ThrowsAsync<PrdbApiException>(() => Client.GetRateLimitAsync(Connection, CancellationToken.None));

        Assert.True(ex.IsUnauthorized);
        Assert.Single(_handler.Requests);
        Assert.Empty(_waits);
        Assert.DoesNotContain("secret-key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_429_waits_for_retry_after_and_tries_again()
    {
        _handler
            .Respond(HttpStatusCode.TooManyRequests, """{"title":"Too many"}""", r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)))
            .Respond(HttpStatusCode.OK, """{"hourly":{"limit":100,"used":1,"remaining":99},"monthly":{"limit":1000,"used":1,"remaining":999}}""");

        var status = await Client.GetRateLimitAsync(Connection, CancellationToken.None);

        Assert.Equal(99, status.Hourly.Remaining);
        Assert.Equal([TimeSpan.FromSeconds(7)], _waits);
    }

    [Fact]
    public async Task A_429_with_a_far_retry_after_fails_instead_of_blocking()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests, null, r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1)));

        var ex = await Assert.ThrowsAsync<PrdbApiException>(() => Client.GetRateLimitAsync(Connection, CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task Server_errors_and_network_failures_are_retried_with_backoff_then_given_up()
    {
        _handler
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Throw(new HttpRequestException("connection refused"))
            .Respond(HttpStatusCode.InternalServerError)
            .Respond(HttpStatusCode.BadGateway);

        var ex = await Assert.ThrowsAsync<PrdbApiException>(() => Client.GetRateLimitAsync(Connection, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(PrdbClient.MaxAttempts, _handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], _waits);
    }

    [Fact]
    public async Task A_transient_failure_followed_by_success_returns_the_answer()
    {
        _handler
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.OK, """{"id":"11111111-1111-1111-1111-111111111111","title":"T","images":[],"actors":[]}""");

        var video = await Client.GetVideoAsync(Connection, Guid.Parse("11111111-1111-1111-1111-111111111111"), CancellationToken.None);

        Assert.Equal("T", video!.Title);
    }

    [Fact]
    public async Task An_unknown_video_or_actor_is_null_rather_than_an_error()
    {
        _handler.Respond(HttpStatusCode.NotFound, """{"title":"Not Found"}""").Respond(HttpStatusCode.NotFound);

        Assert.Null(await Client.GetVideoAsync(Connection, Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await Client.GetActorAsync(Connection, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task A_base_url_with_a_path_prefix_keeps_it()
    {
        _handler.Respond(HttpStatusCode.OK, """{"id":"11111111-1111-1111-1111-111111111111","name":"N","images":[],"bios":[]}""");
        var connection = new PrdbConnection(new Uri("https://proxy.example.test/prdb"), "k");

        await Client.GetActorAsync(connection, Guid.Parse("11111111-1111-1111-1111-111111111111"), CancellationToken.None);

        Assert.Equal("https://proxy.example.test/prdb/actors/11111111-1111-1111-1111-111111111111", _handler.Requests[0].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task An_unreadable_body_is_a_prdb_error()
    {
        _handler.Respond(HttpStatusCode.OK, "<html>maintenance</html>");

        await Assert.ThrowsAsync<PrdbApiException>(() => Client.GetRateLimitAsync(Connection, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "https://api.prdb.net/")]
    [InlineData("", "https://api.prdb.net/")]
    [InlineData(" https://other.example/ ", "https://other.example/")]
    public void A_connection_is_built_from_configuration(string? baseUrl, string expected)
    {
        var connection = PrdbConnection.From(baseUrl, " the-key ");

        Assert.Equal(expected, connection!.BaseUrl.AbsoluteUri);
        Assert.Equal("the-key", connection.ApiKey);
        Assert.DoesNotContain("the-key", connection.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://api.prdb.net", null)]
    [InlineData("https://api.prdb.net", "  ")]
    [InlineData("ftp://api.prdb.net", "k")]
    [InlineData("not a url", "k")]
    public void Without_a_key_or_with_a_bad_url_there_is_no_connection(string baseUrl, string? apiKey)
    {
        Assert.Null(PrdbConnection.From(baseUrl, apiKey));
    }
}
