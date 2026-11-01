using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// Where the plugin keeps what it remembers between runs: <c>&lt;jellyfin data&gt;/prdb</c>.
/// </summary>
public sealed class PrdbDataDirectory
{
    /// <summary>Initializes a new instance of the <see cref="PrdbDataDirectory"/> class.</summary>
    /// <param name="applicationPaths">Jellyfin's paths.</param>
    public PrdbDataDirectory(IApplicationPaths applicationPaths)
        : this(System.IO.Path.Combine(applicationPaths?.DataPath ?? throw new System.ArgumentNullException(nameof(applicationPaths)), "prdb"))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PrdbDataDirectory"/> class at a given path.</summary>
    /// <param name="path">The directory.</param>
    public PrdbDataDirectory(string path)
    {
        Path = path;
    }

    /// <summary>Gets the directory.</summary>
    public string Path { get; }
}
