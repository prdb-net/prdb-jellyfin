using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Prdb.Configuration;

/// <summary>
/// The plugin's settings, as the administrator saves them on the configuration page.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the prdb API key of the administrator's own prdb account. Without it the plugin does nothing.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base URL of the prdb API. Blank means <c>https://api.prdb.net</c>.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.prdb.net";

    /// <summary>
    /// Gets or sets a value indicating whether a match on a file hash (confidence Exact) is applied automatically.
    /// </summary>
    public bool AcceptHashMatches { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a match on a file name prdb has stored for this exact file
    /// (confidence Strong) is applied automatically.
    /// </summary>
    public bool AcceptExactFilenameMatches { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a match read out of the file name, a scene release name or
    /// site, date and title (confidence Probable), is applied automatically. Names can collide.
    /// </summary>
    public bool AcceptNameHeuristicMatches { get; set; } = true;
}
