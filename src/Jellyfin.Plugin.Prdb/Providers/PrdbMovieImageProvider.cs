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
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Providers;

/// <summary>
/// Offers a movie's prdb images: the oldest as primary image, the others as backdrops.
/// </summary>
public class PrdbMovieImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly VideoSource _videos;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PrdbMovieImageProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="PrdbMovieImageProvider"/> class.</summary>
    /// <param name="videos">Gets videos by id.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public PrdbMovieImageProvider(VideoSource videos, IHttpClientFactory httpClientFactory, ILogger<PrdbMovieImageProvider> logger)
    {
        _videos = videos;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PrdbIds.ProviderName;

    /// <inheritdoc />
    public int Order => -1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is Movie;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary, ImageType.Backdrop];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var connection = PrdbSettings.TryGetConnection(PrdbSettings.Current, _logger);
        if (connection is null || !PrdbIdParser.TryGet(item, out var videoId))
        {
            return [];
        }

        try
        {
            var video = await _videos.GetAsync(connection, videoId, cancellationToken).ConfigureAwait(false);
            return video is null ? [] : ToRemoteImages(VideoImages.Ranked(video.Images), Name);
        }
        catch (PrdbApiException ex)
        {
            _logger.LogWarning("prdb images for video {VideoId} are unavailable: {Reason}", videoId, ex.Message);
            return [];
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken) =>
        _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);

    /// <summary>Turns the distribution into Jellyfin's remote images.</summary>
    /// <param name="images">The distribution.</param>
    /// <param name="providerName">The provider name.</param>
    /// <returns>The remote images, primary first.</returns>
    internal static IEnumerable<RemoteImageInfo> ToRemoteImages(VideoImages images, string providerName)
    {
        var result = new List<RemoteImageInfo>();
        if (images.Primary is { Url: { } primaryUrl })
        {
            result.Add(new RemoteImageInfo { ProviderName = providerName, Url = primaryUrl, Type = ImageType.Primary });
        }

        result.AddRange(images.Backdrops
            .Where(image => image.Url is not null)
            .Select(image => new RemoteImageInfo { ProviderName = providerName, Url = image.Url, Type = ImageType.Backdrop }));
        return result;
    }
}
