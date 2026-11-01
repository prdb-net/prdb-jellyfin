using System;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Prdb.Providers;

/// <summary>
/// Reads the prdb id an item carries. Administrators may paste the whole prdb.net address into the
/// metadata editor, so the last path segment of a URL counts as well.
/// </summary>
public static class PrdbIdParser
{
    /// <summary>Reads the prdb id of an item.</summary>
    /// <param name="item">The item or lookup info.</param>
    /// <param name="id">The id.</param>
    /// <returns><c>true</c> when the item carries a valid prdb id.</returns>
    public static bool TryGet(IHasProviderIds item, out Guid id)
    {
        ArgumentNullException.ThrowIfNull(item);
        id = Guid.Empty;
        return item.TryGetProviderId(PrdbIds.ProviderKey, out var value) && TryParse(value, out id);
    }

    /// <summary>Parses a prdb id, bare or as the last segment of a URL.</summary>
    /// <param name="value">The text.</param>
    /// <param name="id">The id.</param>
    /// <returns><c>true</c> when it is a valid, non-empty id.</returns>
    public static bool TryParse(string? value, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().TrimEnd('/');
        var slash = text.LastIndexOf('/');
        if (slash >= 0)
        {
            text = text[(slash + 1)..];
        }

        return Guid.TryParse(text, out id) && id != Guid.Empty;
    }
}
