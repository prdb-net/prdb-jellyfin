using System.Net;
using System.Xml.Serialization;
using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Api.Controllers;
using Jellyfin.Plugin.Prdb.Configuration;
using Jellyfin.Plugin.Prdb.Tests.Api;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Tests.Configuration;

public class ConfigurationTests
{
    [Fact]
    public void A_fresh_configuration_has_no_key_the_public_api_and_every_kind_of_match_switched_on()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(string.Empty, configuration.ApiKey);
        Assert.Equal("https://api.prdb.net", configuration.BaseUrl);
        Assert.True(configuration.AcceptHashMatches);
        Assert.True(configuration.AcceptExactFilenameMatches);
        Assert.True(configuration.AcceptNameHeuristicMatches);
    }

    [Fact]
    public void The_configuration_survives_the_xml_round_trip_jellyfin_stores_it_with()
    {
        var saved = new PluginConfiguration
        {
            ApiKey = "key",
            BaseUrl = "https://proxy.example",
            AcceptHashMatches = true,
            AcceptExactFilenameMatches = false,
            AcceptNameHeuristicMatches = false,
        };
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var buffer = new MemoryStream();

        serializer.Serialize(buffer, saved);
        buffer.Position = 0;
        var loaded = (PluginConfiguration)serializer.Deserialize(buffer)!;

        Assert.Equal("key", loaded.ApiKey);
        Assert.Equal("https://proxy.example", loaded.BaseUrl);
        Assert.True(loaded.AcceptHashMatches);
        Assert.False(loaded.AcceptExactFilenameMatches);
        Assert.False(loaded.AcceptNameHeuristicMatches);
    }

    [Fact]
    public void A_missing_key_is_logged_once_not_for_every_item()
    {
        var logger = new CountingLogger();
        var configuration = new PluginConfiguration();

        Assert.Null(PrdbSettings.TryGetConnection(configuration, logger));
        Assert.Null(PrdbSettings.TryGetConnection(configuration, logger));

        Assert.Equal(1, logger.Warnings);

        configuration.ApiKey = "key";
        Assert.NotNull(PrdbSettings.TryGetConnection(configuration, logger));
    }

    [Theory]
    [InlineData(null, "https://api.prdb.net", "missingKey")]
    [InlineData("key", "ftp://nope", "invalidUrl")]
    public async Task The_connection_test_reports_bad_input_without_calling_prdb(string? apiKey, string baseUrl, string outcome)
    {
        var handler = new FakeHandler();
        var tester = new ConnectionTester(new PrdbClient(new FakeHttpClientFactory(handler)));

        var result = await tester.TestAsync(new ConnectionTestRequest { ApiKey = apiKey, BaseUrl = baseUrl }, CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_connection_test_reports_the_remaining_budget()
    {
        var handler = new FakeHandler().Respond(HttpStatusCode.OK, """{"hourly":{"limit":500,"used":20,"remaining":480},"monthly":{"limit":9000,"used":20,"remaining":8980}}""");
        var tester = new ConnectionTester(new PrdbClient(new FakeHttpClientFactory(handler)));

        var result = await tester.TestAsync(new ConnectionTestRequest { ApiKey = "key", BaseUrl = "https://api.example.test" }, CancellationToken.None);

        Assert.Equal("ok", result.Outcome);
        Assert.Equal(480, result.HourlyRemaining);
        Assert.Equal("https://api.example.test/rate-limit", handler.Requests[0].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task The_connection_test_tells_a_rejected_key_apart()
    {
        var handler = new FakeHandler().Respond(HttpStatusCode.Unauthorized);
        var tester = new ConnectionTester(new PrdbClient(new FakeHttpClientFactory(handler)));

        var result = await tester.TestAsync(new ConnectionTestRequest { ApiKey = "wrong" }, CancellationToken.None);

        Assert.Equal("unauthorized", result.Outcome);
    }

    [Fact]
    public async Task The_connection_test_tells_an_unreachable_api_apart()
    {
        var handler = new FakeHandler();
        for (var i = 0; i < PrdbClient.MaxAttempts; i++)
        {
            handler.Throw(new HttpRequestException("no route to host"));
        }

        var tester = new ConnectionTester(new PrdbClient(new FakeHttpClientFactory(handler), (_, _) => Task.CompletedTask));

        var result = await tester.TestAsync(new ConnectionTestRequest { ApiKey = "key" }, CancellationToken.None);

        Assert.Equal("unreachable", result.Outcome);
    }

    private sealed class CountingLogger : ILogger
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings++;
            }
        }
    }
}
