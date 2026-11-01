using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Configuration;
using Jellyfin.Plugin.Prdb.Identification;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Providers;

/// <summary>
/// The prdb metadata provider for movies. A movie with a prdb id gets that video's metadata; a movie
/// without one is identified on the spot by the same rules as the batch run, so a newly added file does
/// not wait for the scheduled task.
/// </summary>
public class PrdbMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
{
    private readonly VideoSource _videos;
    private readonly Identifier _identifier;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PrdbMovieProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="PrdbMovieProvider"/> class.</summary>
    /// <param name="videos">Gets videos by id.</param>
    /// <param name="identifier">The identifier.</param>
    /// <param name="httpClientFactory">The HTTP client factory, for images.</param>
    /// <param name="logger">The logger.</param>
    public PrdbMovieProvider(VideoSource videos, Identifier identifier, IHttpClientFactory httpClientFactory, ILogger<PrdbMovieProvider> logger)
    {
        _videos = videos;
        _identifier = identifier;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PrdbIds.ProviderName;

    /// <inheritdoc />
    /// <remarks>Ahead of the general-purpose movie databases, which do not know these videos.</remarks>
    public int Order => -1;

    /// <inheritdoc />
    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);

        var configuration = PrdbSettings.Current;
        var connection = PrdbSettings.TryGetConnection(configuration, _logger);
        if (connection is null)
        {
            return new MetadataResult<Movie>();
        }

        try
        {
            var video = PrdbIdParser.TryGet(info, out var videoId)
                ? await _videos.GetAsync(connection, videoId, cancellationToken).ConfigureAwait(false)
                : string.IsNullOrEmpty(info.Path)
                    ? null
                    : await _identifier.IdentifyOneAsync(connection, configuration, info.Path, cancellationToken).ConfigureAwait(false);

            return video is null ? new MetadataResult<Movie>() : VideoMetadataMapper.Map(video);
        }
        catch (PrdbApiException ex)
        {
            _logger.LogWarning("prdb metadata for {Path} is unavailable: {Reason}", info.Path, ex.Message);
            return new MetadataResult<Movie>();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// There is no search by name: the plugin identifies by hash and file name only. A search that carries a
    /// prdb id, as Jellyfin's identify dialog sends when an id is entered, returns that video.
    /// </remarks>
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(searchInfo);

        var connection = PrdbSettings.TryGetConnection(PrdbSettings.Current, _logger);
        if (connection is null || !PrdbIdParser.TryGet(searchInfo, out var videoId))
        {
            return [];
        }

        try
        {
            var video = await _videos.GetAsync(connection, videoId, cancellationToken).ConfigureAwait(false);
            if (video is null)
            {
                return [];
            }

            var result = new RemoteSearchResult
            {
                Name = video.Title,
                Overview = video.Description,
                SearchProviderName = Name,
                ImageUrl = VideoImages.Ranked(video.Images).Primary?.Url,
                PremiereDate = video.ReleaseDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                ProductionYear = video.ReleaseDate?.Year,
            };
            result.ProviderIds[PrdbIds.ProviderKey] = video.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
            return [result];
        }
        catch (PrdbApiException ex)
        {
            _logger.LogWarning("prdb lookup of video {VideoId} failed: {Reason}", videoId, ex.Message);
            return [];
        }
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken) =>
        _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
}
