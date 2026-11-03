using Jellyfin.Plugin.Prdb.Collections;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Prdb.ExternalIds;

/// <summary>
/// Marks the collections the plugin keeps per studio. Registering it also makes Jellyfin keep the mark when
/// it reads a collection back from its <c>collection.xml</c>, which it does only for known provider ids.
/// </summary>
public class PrdbStudioExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "prdb studio";

    /// <inheritdoc />
    public string Key => StudioCollections.ProviderKey;

    /// <inheritdoc />
    /// <remarks>None, so the metadata editor labels the field <em>prdb studio Id</em> and not <em>prdb studio Box Set Id</em>.</remarks>
    public ExternalIdMediaType? Type => null;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is BoxSet;
}
