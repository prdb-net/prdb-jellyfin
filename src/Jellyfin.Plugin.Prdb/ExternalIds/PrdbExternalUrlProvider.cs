using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Prdb.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Prdb.ExternalIds;

/// <summary>
/// Links movies and people with a prdb id to their pages on prdb.net.
/// </summary>
public class PrdbExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc />
    public string Name => PrdbIds.ProviderName;

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is null || !PrdbIdParser.TryGet(item, out var id))
        {
            yield break;
        }

        var format = item switch
        {
            Movie => PrdbIds.VideoUrlFormat,
            Person => PrdbIds.ActorUrlFormat,
            _ => null,
        };

        if (format is not null)
        {
            yield return string.Format(CultureInfo.InvariantCulture, format, id.ToString("D", CultureInfo.InvariantCulture));
        }
    }
}
