using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Prdb.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Prdb.Providers;

/// <summary>
/// Maps a prdb video onto a Jellyfin movie.
/// </summary>
public static class VideoMetadataMapper
{
    /// <summary>The provider id key StashDB scene ids are stored under.</summary>
    public const string StashDbProviderKey = "StashDB";

    /// <summary>Builds the metadata result for a video.</summary>
    /// <param name="video">The prdb video.</param>
    /// <returns>The result, with <see cref="MetadataResult{T}.HasMetadata"/> set.</returns>
    public static MetadataResult<Movie> Map(VideoDetail video)
    {
        ArgumentNullException.ThrowIfNull(video);

        var movie = new Movie
        {
            Name = video.Title,
            Overview = string.IsNullOrWhiteSpace(video.Description) ? null : video.Description.Trim(),
        };

        movie.SetProviderId(PrdbIds.ProviderKey, Format(video.Id));
        if (video.StashdbSceneId is { } stashId)
        {
            movie.SetProviderId(StashDbProviderKey, Format(stashId));
        }

        if (video.ReleaseDate is { } date)
        {
            movie.PremiereDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            movie.ProductionYear = date.Year;
        }

        foreach (var studio in Studios(video))
        {
            movie.AddStudio(studio);
        }

        var result = new MetadataResult<Movie>
        {
            Item = movie,
            HasMetadata = true,
        };

        foreach (var actor in video.Actors ?? [])
        {
            var person = new PersonInfo
            {
                Name = actor.Name,
                Type = PersonKind.Actor,
                ImageUrl = ActorImages.Best(actor.Images)?.Url,
            };
            person.SetProviderId(PrdbIds.ProviderKey, Format(actor.Id));
            result.AddPerson(person);
        }

        return result;
    }

    /// <summary>The site, and its network when it has a different name.</summary>
    /// <param name="video">The video.</param>
    /// <returns>The studios in that order.</returns>
    internal static IEnumerable<string> Studios(VideoDetail video)
    {
        var site = video.Site?.Title;
        if (!string.IsNullOrWhiteSpace(site))
        {
            yield return site.Trim();
        }

        var network = video.Site?.Network?.Title;
        if (!string.IsNullOrWhiteSpace(network) && !string.Equals(network.Trim(), site?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            yield return network.Trim();
        }
    }

    private static string Format(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);
}
