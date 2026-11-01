using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Prdb.Api;

/// <summary>
/// A small hand-written client for the prdb public API. It deliberately does not use the generated SDK, so
/// nothing beyond the framework is loaded into the Jellyfin process.
/// </summary>
public sealed class PrdbClient : IPrdbClient
{
    /// <summary>The most files <c>POST /videos/identify</c> accepts in one request.</summary>
    public const int MaxIdentifyBatchSize = 200;

    /// <summary>The name of the <see cref="HttpClient"/> the plugin asks Jellyfin's factory for.</summary>
    public const string HttpClientName = "prdb";

    /// <summary>How many times a request is sent at most, the first attempt included.</summary>
    internal const int MaxAttempts = 4;

    /// <summary>The longest wait a <c>Retry-After</c> header is honoured for; a longer one fails the request.</summary>
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(2);

    /// <summary>The wait after a 429 that carries no usable <c>Retry-After</c>.</summary>
    internal static readonly TimeSpan DefaultRateLimitDelay = TimeSpan.FromSeconds(10);

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string UserAgent = "Jellyfin.Plugin.Prdb/"
        + (typeof(PrdbClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
           ?? typeof(PrdbClient).Assembly.GetName().Version?.ToString()
           ?? "0");

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrdbClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's HTTP client factory.</param>
    public PrdbClient(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, Task.Delay)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrdbClient"/> class with a replaceable wait, for tests.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="delay">Waits between attempts.</param>
    internal PrdbClient(IHttpClientFactory httpClientFactory, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _httpClientFactory = httpClientFactory;
        _delay = delay;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(
        PrdbConnection connection,
        IReadOnlyList<IdentifyFile> files,
        bool includeVideoDetails,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(files);

        var results = new List<IdentifyResult>(files.Count);
        foreach (var batch in files.Chunk(MaxIdentifyBatchSize))
        {
            var body = new IdentifyRequest(batch, includeVideoDetails);
            var response = await SendAsync<IdentifyResponse>(
                connection,
                () => new HttpRequestMessage(HttpMethod.Post, connection.Resolve("videos/identify"))
                {
                    // Buffered with a Content-Length rather than streamed chunked: some proxies in front of
                    // an API refuse chunked request bodies, and the body is small.
                    Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions))
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" } },
                    },
                },
                allowNotFound: false,
                cancellationToken).ConfigureAwait(false);

            if (response is null || response.Results.Count != batch.Length)
            {
                throw new PrdbApiException("prdb answered an identify request with a result list that does not match the files sent.");
            }

            results.AddRange(response.Results);
        }

        return results;
    }

    /// <inheritdoc />
    public Task<VideoDetail?> GetVideoAsync(PrdbConnection connection, Guid videoId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return SendAsync<VideoDetail>(
            connection,
            () => new HttpRequestMessage(HttpMethod.Get, connection.Resolve("videos/" + videoId.ToString("D", CultureInfo.InvariantCulture))),
            allowNotFound: true,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ActorDetail?> GetActorAsync(PrdbConnection connection, Guid actorId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return SendAsync<ActorDetail>(
            connection,
            () => new HttpRequestMessage(HttpMethod.Get, connection.Resolve("actors/" + actorId.ToString("D", CultureInfo.InvariantCulture))),
            allowNotFound: true,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RateLimitStatus> GetRateLimitAsync(PrdbConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var status = await SendAsync<RateLimitStatus>(
            connection,
            () => new HttpRequestMessage(HttpMethod.Get, connection.Resolve("rate-limit")),
            allowNotFound: false,
            cancellationToken).ConfigureAwait(false);
        return status ?? throw new PrdbApiException("prdb answered the rate-limit request with an empty body.");
    }

    /// <summary>How long to wait before the next attempt after a 429, or <c>null</c> when waiting is pointless.</summary>
    /// <param name="response">The 429 response.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The wait, or <c>null</c>.</returns>
    internal static TimeSpan? RateLimitDelay(HttpResponseMessage response, DateTimeOffset now)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan wait;
        if (retryAfter?.Delta is { } delta)
        {
            wait = delta;
        }
        else if (retryAfter?.Date is { } date)
        {
            wait = date - now;
        }
        else
        {
            return DefaultRateLimitDelay;
        }

        if (wait < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        // A window that resets in an hour is not worth blocking a library scan for; the request fails and
        // the next run picks the file up again.
        return wait <= MaxRetryAfter ? wait : null;
    }

    /// <summary>The wait before attempt <paramref name="attempt"/> + 1 after a transient failure.</summary>
    /// <param name="attempt">The attempt that failed, starting at 1.</param>
    /// <returns>1, 2, 4 … seconds.</returns>
    internal static TimeSpan BackoffDelay(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
        || (int)status >= 500;

    private async Task<T?> SendAsync<T>(
        PrdbConnection connection,
        Func<HttpRequestMessage> createRequest,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where T : class
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);

        for (var attempt = 1; ; attempt++)
        {
            using var request = createRequest();
            request.Headers.Add("X-Api-Key", connection.ApiKey);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await _delay(BackoffDelay(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw new PrdbApiException("prdb could not be reached at " + connection.BaseUrl.AbsoluteUri + ".", ex);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                // HttpClient reports its own timeout as a cancellation.
                await _delay(BackoffDelay(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PrdbApiException("prdb did not answer in time.", ex);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
                    }
                    catch (JsonException ex)
                    {
                        throw new PrdbApiException("prdb answered with a body this plugin cannot read.", ex);
                    }
                }

                var status = response.StatusCode;
                if (status == HttpStatusCode.NotFound && allowNotFound)
                {
                    return null;
                }

                if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new PrdbApiException("prdb rejected the API key (HTTP " + (int)status + "). Check the key in the plugin settings.", status);
                }

                if (status == HttpStatusCode.TooManyRequests && attempt < MaxAttempts
                    && RateLimitDelay(response, DateTimeOffset.UtcNow) is { } wait)
                {
                    await _delay(wait, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (IsTransient(status) && attempt < MaxAttempts)
                {
                    await _delay(BackoffDelay(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new PrdbApiException(
                    status == HttpStatusCode.TooManyRequests
                        ? "The prdb request budget of this account is used up for now (HTTP 429)."
                        : "prdb answered HTTP " + (int)status + ".",
                    status);
            }
        }
    }
}
