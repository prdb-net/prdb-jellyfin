using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Prdb.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Prdb;

/// <summary>
/// The prdb plugin: identifies local videos against prdb.net and fills in their metadata.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The plugin's identity. It never changes, because Jellyfin keys installed plugins and their configuration by it.
    /// </summary>
    public static readonly Guid PluginId = Guid.Parse("74e0efe4-afea-43e4-8b19-1cf114c7755b");

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "prdb";

    /// <inheritdoc />
    public override string Description => "Identifies videos by file hash and file name against prdb.net and fills in their metadata. Read-only.";

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
