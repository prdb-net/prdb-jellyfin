using System.Collections.Generic;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Prdb;

/// <summary>
/// The movies the plugin looks at: those in libraries that have the prdb metadata provider switched on.
/// </summary>
/// <param name="libraryManager">Jellyfin's library.</param>
/// <param name="baseItemManager">Decides which providers are switched on for an item.</param>
public sealed class PrdbLibraryScope(ILibraryManager libraryManager, IBaseItemManager baseItemManager)
{
    /// <summary>Gets the movies with a file in libraries that have prdb switched on.</summary>
    /// <returns>The movies.</returns>
    public IEnumerable<Movie> Movies()
    {
        var items = libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            IsVirtualItem = false,
            Recursive = true,
        });

        foreach (var item in items)
        {
            if (item is Movie movie && !string.IsNullOrEmpty(movie.Path) && Includes(movie))
            {
                yield return movie;
            }
        }
    }

    /// <summary>Whether prdb is switched on for the item's library.</summary>
    /// <param name="item">The item.</param>
    /// <returns><c>true</c> when it is.</returns>
    public bool Includes(BaseItem item)
    {
        // The same rule Jellyfin applies before it calls a metadata provider, so the plugin never touches an
        // item prdb would not have been asked about during a refresh.
        var typeOptions = libraryManager.GetLibraryOptions(item).GetTypeOptions(nameof(Movie));
        return baseItemManager.IsMetadataFetcherEnabled(item, typeOptions, PrdbIds.ProviderName);
    }
}
