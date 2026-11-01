using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Providers;

/// <summary>
/// Offers the prdb images of a performer with a prdb actor id: posters first, then face crops, then thumbnails.
/// </summary>
public class PrdbPersonImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IPrdbClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PrdbPersonImageProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="PrdbPersonImageProvider"/> class.</summary>
    /// <param name="client">The prdb client.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public PrdbPersonImageProvider(IPrdbClient client, IHttpClientFactory httpClientFactory, ILogger<PrdbPersonImageProvider> logger)
    {
        _client = client;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PrdbIds.ProviderName;

    /// <inheritdoc />
    public int Order => -1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Person;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var connection = PrdbSettings.TryGetConnection(PrdbSettings.Current, _logger);
        if (connection is null || !PrdbIdParser.TryGet(item, out var actorId))
        {
            return [];
        }

        try
        {
            var actor = await _client.GetActorAsync(connection, actorId, cancellationToken).ConfigureAwait(false);
            return ActorImages.Ranked(actor?.Images)
                .Select(image => new RemoteImageInfo { ProviderName = Name, Url = image.Url, Type = ImageType.Primary })
                .ToList();
        }
        catch (PrdbApiException ex)
        {
            _logger.LogWarning("prdb images for actor {ActorId} are unavailable: {Reason}", actorId, ex.Message);
            return [];
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken) =>
        _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
}
