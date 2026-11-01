using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Prdb.ExternalIds;

/// <summary>
/// Shows the prdb actor id of a person in the metadata editor.
/// </summary>
public class PrdbPersonExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => PrdbIds.ProviderName;

    /// <inheritdoc />
    public string Key => PrdbIds.ProviderKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Person;
}
