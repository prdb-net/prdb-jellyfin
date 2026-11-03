using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Prdb.Collections;

/// <summary>
/// Gives a studio collection the cover of its newest video. Jellyfin draws no image for collections
/// itself, and prdb's site images are not among the endpoints the plugin reads.
/// </summary>
public class StudioCollectionImageProvider : IDynamicImageProvider
{
    /// <inheritdoc />
    public string Name => PrdbIds.ProviderName;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => StudioCollections.IsStudioCollection(item);

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetImage(BaseItem item, ImageType type, CancellationToken cancellationToken)
    {
        var cover = item is BoxSet collection && type == ImageType.Primary
            ? collection.GetLinkedChildren()
                .OfType<Movie>()
                .Where(HasLocalCover)
                .OrderByDescending(m => m.PremiereDate ?? DateTime.MinValue)
                .ThenByDescending(m => m.DateCreated)
                .FirstOrDefault()
            : null;

        return Task.FromResult(cover is null
            ? new DynamicImageResponse { HasImage = false }
            : new DynamicImageResponse
            {
                HasImage = true,
                Path = cover.GetImagePath(ImageType.Primary, 0),
                Protocol = MediaProtocol.File,
            });
    }

    /// <summary>Whether the movie has a primary image stored on disk.</summary>
    /// <param name="movie">The movie.</param>
    /// <returns><c>true</c> when it has.</returns>
    internal static bool HasLocalCover(Movie movie) =>
        movie.GetImageInfo(ImageType.Primary, 0) is { IsLocalFile: true };
}
