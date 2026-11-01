using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Identification;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Providers;

/// <summary>
/// Gets a video by id, from the cache identification filled or from prdb. Shared by the metadata and the
/// image provider, which Jellyfin calls one after the other for the same item.
/// </summary>
public sealed class VideoSource
{
    private readonly IPrdbClient _client;
    private readonly VideoCache _cache;
    private readonly ILogger<VideoSource> _logger;

    /// <summary>Initializes a new instance of the <see cref="VideoSource"/> class.</summary>
    /// <param name="client">The prdb client.</param>
    /// <param name="cache">The video cache.</param>
    /// <param name="logger">The logger.</param>
    public VideoSource(IPrdbClient client, VideoCache cache, ILogger<VideoSource> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Gets a video, or <c>null</c> when prdb does not know the id.</summary>
    /// <param name="connection">Where and as whom to ask.</param>
    /// <param name="videoId">The video id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The video, or <c>null</c>.</returns>
    public async Task<VideoDetail?> GetAsync(PrdbConnection connection, Guid videoId, CancellationToken cancellationToken)
    {
        var cached = _cache.TryGet(videoId, DateTimeOffset.UtcNow);
        if (cached is not null)
        {
            return cached;
        }

        var video = await _client.GetVideoAsync(connection, videoId, cancellationToken).ConfigureAwait(false);
        if (video is null)
        {
            _logger.LogWarning("prdb does not know video {VideoId}; check the prdb id of the item", videoId);
            return null;
        }

        _cache.Put(video);
        return video;
    }
}
