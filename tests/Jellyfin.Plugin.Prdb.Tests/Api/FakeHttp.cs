using System.Net;
using System.Text;

namespace Jellyfin.Plugin.Prdb.Tests.Api;

/// <summary>A handler that answers from a queue of responses and records every request it saw.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public FakeHandler Respond(HttpStatusCode status, string? json = null, Action<HttpResponseMessage>? configure = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    public FakeHandler Throw(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    public FakeHandler RespondWith(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        _responses.Enqueue(request => respond(request, Requests[^1].Body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No response queued for " + request.RequestUri);
        }

        return _responses.Dequeue()(request);
    }
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
