using System.Threading;
using Jellyfin.Plugin.Prdb.Api;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Configuration;

/// <summary>
/// Reads the saved configuration into the values the rest of the plugin works with.
/// </summary>
internal static class PrdbSettings
{
    private static int _missingKeyLogged;

    /// <summary>Gets the saved configuration, or defaults while the plugin is not loaded (tests).</summary>
    public static PluginConfiguration Current => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Builds the connection from the saved configuration. Without a usable key or URL it returns <c>null</c>
    /// and says so in the log once, not on every item a scan touches.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <param name="logger">Where to say that the key is missing.</param>
    /// <returns>The connection, or <c>null</c>.</returns>
    public static PrdbConnection? TryGetConnection(PluginConfiguration configuration, ILogger logger)
    {
        var connection = PrdbConnection.From(configuration.BaseUrl, configuration.ApiKey);
        if (connection is null)
        {
            if (Interlocked.Exchange(ref _missingKeyLogged, 1) == 0)
            {
                logger.LogWarning("prdb is not configured: enter a prdb API key (and a valid base URL) in the plugin settings. Nothing is identified until then.");
            }

            return null;
        }

        Interlocked.Exchange(ref _missingKeyLogged, 0);
        return connection;
    }
}
