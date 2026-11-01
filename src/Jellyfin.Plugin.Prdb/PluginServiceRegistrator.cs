using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Api.Controllers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Prdb;

/// <summary>
/// Registers the plugin's services with Jellyfin's container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IPrdbClient, PrdbClient>();
        serviceCollection.AddSingleton<ConnectionTester>();
    }
}
