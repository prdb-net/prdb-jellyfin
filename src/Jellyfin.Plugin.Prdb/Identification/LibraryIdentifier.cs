using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Prdb.Collections;
using Jellyfin.Plugin.Prdb.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// Runs identification over Jellyfin's library: finds the movies without a prdb id in libraries that have
/// the prdb metadata provider switched on, identifies them in batches, stores the prdb id on every match
/// the switches allow and queues a metadata refresh for it.
/// </summary>
public sealed class LibraryIdentifier
{
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    private readonly ILibraryManager _libraryManager;
    private readonly PrdbLibraryScope _scope;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly Identifier _identifier;
    private readonly StudioCollections _studioCollections;
    private readonly ILogger<LibraryIdentifier> _logger;

    /// <summary>Initializes a new instance of the <see cref="LibraryIdentifier"/> class.</summary>
    /// <param name="libraryManager">Jellyfin's library.</param>
    /// <param name="scope">The movies the plugin looks at.</param>
    /// <param name="providerManager">Jellyfin's metadata providers.</param>
    /// <param name="fileSystem">Jellyfin's file system.</param>
    /// <param name="identifier">The identifier.</param>
    /// <param name="studioCollections">Keeps the studio collections.</param>
    /// <param name="logger">The logger.</param>
    public LibraryIdentifier(
        ILibraryManager libraryManager,
        PrdbLibraryScope scope,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        Identifier identifier,
        StudioCollections studioCollections,
        ILogger<LibraryIdentifier> logger)
    {
        _libraryManager = libraryManager;
        _scope = scope;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _identifier = identifier;
        _studioCollections = studioCollections;
        _logger = logger;
    }

    /// <summary>Identifies the library's movies that have no prdb id yet.</summary>
    /// <param name="recheckStale">Whether files whose remembered answer is stale are asked about again.</param>
    /// <param name="progress">Progress from 0 to 100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task RunAsync(bool recheckStale, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var configuration = PrdbSettings.Current;
        var connection = PrdbSettings.TryGetConnection(configuration, _logger);
        if (connection is null)
        {
            progress?.Report(100);
            return;
        }

        // The scheduled task and the post-scan task may overlap; one run at a time is enough.
        if (!await RunLock.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("prdb identification is already running; skipping this run");
            progress?.Report(100);
            return;
        }

        try
        {
            var candidates = FindCandidates();
            _logger.LogInformation("prdb identification: {Count} movies without a prdb id", candidates.Count);

            var summary = await _identifier.IdentifyAsync(
                connection,
                configuration,
                candidates,
                ApplyAsync,
                recheckStale,
                progress,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("prdb identification finished: {Summary}", summary);
        }
        finally
        {
            RunLock.Release();
        }

        // Matches applied just now only reach their studios once the queued refreshes are done; the
        // collections follow those through StudioCollectionWatcher. This catches everything else.
        await _studioCollections.UpdateAsync(cancellationToken).ConfigureAwait(false);
    }

    private List<IdentificationCandidate> FindCandidates()
    {
        var candidates = new List<IdentificationCandidate>();
        foreach (var movie in _scope.Movies())
        {
            if (!movie.HasProviderId(PrdbIds.ProviderKey))
            {
                candidates.Add(new IdentificationCandidate(movie.Id, movie.Path));
            }
        }

        return candidates;
    }

    private async Task ApplyAsync(IdentificationCandidate candidate, Guid videoId, CancellationToken cancellationToken)
    {
        if (_libraryManager.GetItemById(candidate.ItemId) is not Movie movie)
        {
            return;
        }

        movie.SetProviderId(PrdbIds.ProviderKey, videoId.ToString("D", CultureInfo.InvariantCulture));
        await movie.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

        // The item was named after its file until now, so its metadata and images are replaced by prdb's.
        // Fields the administrator locked stay as they are.
        _providerManager.QueueRefresh(
            movie.Id,
            new MetadataRefreshOptions(new DirectoryService(_fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = true,
                ReplaceAllImages = true,
            },
            RefreshPriority.Normal);
    }
}
