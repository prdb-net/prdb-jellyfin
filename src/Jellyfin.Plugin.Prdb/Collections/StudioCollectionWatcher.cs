using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Collections;

/// <summary>
/// Updates the studio collections shortly after identified movies change or disappear, and after the
/// settings are saved. Identification only queues a metadata refresh, so a new match reaches its studios
/// some time after the run that found it; this is what puts it into its collections then.
/// </summary>
public sealed class StudioCollectionWatcher : IHostedService, IDisposable
{
    /// <summary>How long things have to be quiet before the collections are updated.</summary>
    internal static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(30);

    private readonly ILibraryManager _libraryManager;
    private readonly StudioCollections _studioCollections;
    private readonly ILogger<StudioCollectionWatcher> _logger;
    private readonly Timer _timer;
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>Initializes a new instance of the <see cref="StudioCollectionWatcher"/> class.</summary>
    /// <param name="libraryManager">Jellyfin's library.</param>
    /// <param name="studioCollections">Keeps the studio collections.</param>
    /// <param name="logger">The logger.</param>
    public StudioCollectionWatcher(ILibraryManager libraryManager, StudioCollections studioCollections, ILogger<StudioCollectionWatcher> logger)
    {
        _libraryManager = libraryManager;
        _studioCollections = studioCollections;
        _logger = logger;
        _timer = new Timer(_ => _ = UpdateAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemChanged;
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged += OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemChanged;
        if (Plugin.Instance is { } plugin)
        {
            plugin.ConfigurationChanged -= OnConfigurationChanged;
        }

        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        await _stopping.CancelAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer.Dispose();
        _stopping.Dispose();
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        // Only identified movies matter. The plugin's own changes are to collections, so they do not come
        // back here.
        if (e.Item is Movie movie && movie.HasProviderId(PrdbIds.ProviderKey))
        {
            Schedule();
        }
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e) => Schedule();

    // Every change pushes the update back, so a scan or an identification run ends in one update.
    private void Schedule() => _timer.Change(QuietPeriod, Timeout.InfiniteTimeSpan);

    private async Task UpdateAsync()
    {
        try
        {
            await _studioCollections.UpdateAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // A failed update must not take the server down; the next change tries again.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "prdb studio collections could not be updated");
        }
    }
}
