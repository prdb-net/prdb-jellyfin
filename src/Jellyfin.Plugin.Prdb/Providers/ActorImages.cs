using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Prdb.Api;

namespace Jellyfin.Plugin.Prdb.Providers;

/// <summary>
/// Picks actor images for Jellyfin, whose person image is a portrait.
/// </summary>
public static class ActorImages
{
    /// <summary>Orders an actor's images best first: posters, then face crops, then thumbnails.</summary>
    /// <param name="images">The actor's images.</param>
    /// <returns>The images with a URL, best first.</returns>
    public static IEnumerable<ActorImage> Ranked(IEnumerable<ActorImage>? images) =>
        (images ?? [])
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .OrderBy(image => image.ImageType switch
            {
                ActorImageType.Poster => 0,
                ActorImageType.Face => 1,
                ActorImageType.Thumbnail => 2,
                _ => 3,
            });

    /// <summary>The best image of an actor, or <c>null</c>.</summary>
    /// <param name="images">The actor's images.</param>
    /// <returns>The image, or <c>null</c>.</returns>
    public static ActorImage? Best(IEnumerable<ActorImage>? images) => Ranked(images).FirstOrDefault();
}
