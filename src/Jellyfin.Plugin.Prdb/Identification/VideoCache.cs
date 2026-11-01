using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Jellyfin.Plugin.Prdb.Api;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// Keeps the video documents identification returned, so that the metadata refresh that follows a match
/// does not fetch the same video from prdb a second time.
/// </summary>
public sealed class VideoCache
{
    /// <summary>How long a cached video is used before it is fetched again.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private readonly string _directory;
    private readonly ILogger<VideoCache> _logger;

    /// <summary>Initializes a new instance of the <see cref="VideoCache"/> class.</summary>
    /// <param name="dataDirectory">The plugin's data directory.</param>
    /// <param name="logger">The logger.</param>
    public VideoCache(PrdbDataDirectory dataDirectory, ILogger<VideoCache> logger)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        _directory = Path.Combine(dataDirectory.Path, "videos");
        _logger = logger;
    }

    /// <summary>Gets a cached video that is younger than <see cref="MaxAge"/>.</summary>
    /// <param name="videoId">The video id.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The video, or <c>null</c>.</returns>
    public VideoDetail? TryGet(Guid videoId, DateTimeOffset now)
    {
        var path = PathOf(videoId);
        try
        {
            if (!File.Exists(path) || now - File.GetLastWriteTimeUtc(path) > MaxAge)
            {
                return null;
            }

            return JsonSerializer.Deserialize<VideoDetail>(File.ReadAllText(path), PrdbClient.JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogDebug(ex, "Ignoring an unreadable cached prdb video {VideoId}", videoId);
            return null;
        }
    }

    /// <summary>Caches a video.</summary>
    /// <param name="video">The video.</param>
    public void Put(VideoDetail video)
    {
        ArgumentNullException.ThrowIfNull(video);
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathOf(video.Id);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(video, PrdbClient.JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not cache prdb video {VideoId}", video.Id);
        }
    }

    private string PathOf(Guid videoId) => Path.Combine(_directory, videoId.ToString("N", CultureInfo.InvariantCulture) + ".json");
}
