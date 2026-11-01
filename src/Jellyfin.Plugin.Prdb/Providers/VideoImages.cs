using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Prdb.Api;

namespace Jellyfin.Plugin.Prdb.Providers;

/// <summary>
/// Distributes a video's images onto Jellyfin's image types. prdb does not label its video images, and
/// lists them oldest first: the oldest is the one the catalogue started with, normally the site's cover,
/// so it becomes the primary image and the rest become backdrops.
/// </summary>
/// <param name="Primary">The image for <c>Primary</c>, or <c>null</c>.</param>
/// <param name="Backdrops">The images for <c>Backdrop</c>.</param>
public sealed record VideoImages(VideoImage? Primary, IReadOnlyList<VideoImage> Backdrops)
{
    /// <summary>Distributes the images.</summary>
    /// <param name="images">The video's images, oldest first, as prdb lists them.</param>
    /// <returns>The distribution.</returns>
    public static VideoImages Ranked(IEnumerable<VideoImage>? images)
    {
        var usable = (images ?? []).Where(image => !string.IsNullOrWhiteSpace(image.Url)).ToList();
        return new VideoImages(usable.FirstOrDefault(), usable.Skip(1).ToList());
    }
}
