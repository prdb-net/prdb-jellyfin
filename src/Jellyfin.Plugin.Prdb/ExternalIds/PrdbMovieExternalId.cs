using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Prdb.ExternalIds;

/// <summary>
/// Shows the prdb video id of a movie in the metadata editor, where it can also be entered by hand.
/// </summary>
public class PrdbMovieExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => PrdbIds.ProviderName;

    /// <inheritdoc />
    public string Key => PrdbIds.ProviderKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Movie;
}
