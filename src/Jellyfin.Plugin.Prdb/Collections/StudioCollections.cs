using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Prdb.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Collections;

/// <summary>
/// Keeps one Jellyfin collection per studio of the identified movies when the administrator switched it on,
/// and removes the plugin's collections when the switch is off. Collections the plugin did not make are
/// never touched.
/// </summary>
public sealed class StudioCollections
{
    /// <summary>The provider id key that marks a collection as the plugin's; its value is the studio.</summary>
    public const string ProviderKey = "PrdbStudio";

    private static readonly SemaphoreSlim UpdateLock = new(1, 1);

    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly PrdbLibraryScope _scope;
    private readonly ILogger<StudioCollections> _logger;

    /// <summary>Initializes a new instance of the <see cref="StudioCollections"/> class.</summary>
    /// <param name="libraryManager">Jellyfin's library.</param>
    /// <param name="collectionManager">Jellyfin's collections.</param>
    /// <param name="providerManager">Jellyfin's metadata providers.</param>
    /// <param name="fileSystem">Jellyfin's file system.</param>
    /// <param name="scope">The movies the plugin looks at.</param>
    /// <param name="logger">The logger.</param>
    public StudioCollections(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        PrdbLibraryScope scope,
        ILogger<StudioCollections> logger)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _scope = scope;
        _logger = logger;
    }

    /// <summary>Brings the studio collections in line with the identified movies and the switch.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task UpdateAsync(CancellationToken cancellationToken)
    {
        await UpdateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var enabled = PrdbSettings.Current.CreateStudioCollections;
            var existing = Existing();
            if (!enabled && existing.Count == 0)
            {
                return;
            }

            var movies = enabled ? IdentifiedMovies() : [];
            var plan = StudioCollectionPlan.For(
                enabled,
                movies.Select(m => new StudioMember(m.Id, m.Studios)),
                existing.Select(Describe));

            if (!plan.IsEmpty)
            {
                await ApplyAsync(plan, existing, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "prdb studio collections: created {Created}, changed {Changed}, deleted {Deleted}",
                    plan.Create.Count,
                    plan.Change.Count,
                    plan.Delete.Count);
            }

            if (enabled)
            {
                QueueMissingImages(movies);
            }
        }
        finally
        {
            UpdateLock.Release();
        }
    }

    /// <summary>Whether a collection is one the plugin keeps.</summary>
    /// <param name="item">The item.</param>
    /// <returns><c>true</c> when it is.</returns>
    internal static bool IsStudioCollection(BaseItem item) =>
        item is BoxSet && !string.IsNullOrWhiteSpace(item.GetProviderId(ProviderKey));

    private static StudioCollection Describe(BoxSet collection) => new(
        collection.Id,
        collection.GetProviderId(ProviderKey)!,
        collection.LinkedChildren.Where(c => c.ItemId.HasValue).Select(c => c.ItemId!.Value).ToHashSet());

    private List<BoxSet> Existing() =>
        _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Recursive = true,
        })
        .Where(IsStudioCollection)
        .Cast<BoxSet>()
        .ToList();

    private List<Movie> IdentifiedMovies() =>
        _scope.Movies().Where(m => m.HasProviderId(PrdbIds.ProviderKey)).ToList();

    private async Task ApplyAsync(StudioCollectionPlan plan, List<BoxSet> existing, CancellationToken cancellationToken)
    {
        foreach (var id in plan.Delete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var collection = existing.First(c => c.Id == id);
            _libraryManager.DeleteItem(collection, new DeleteOptions { DeleteFileLocation = true }, notifyParentItem: true);
        }

        foreach (var change in plan.Change)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Added first: a collection must never be left with nothing in it, not even for a moment.
            if (change.Add.Count > 0)
            {
                await _collectionManager.AddToCollectionAsync(change.CollectionId, change.Add).ConfigureAwait(false);
            }

            if (change.Remove.Count > 0)
            {
                await _collectionManager.RemoveFromCollectionAsync(change.CollectionId, change.Remove).ConfigureAwait(false);
            }
        }

        foreach (var creation in plan.Create)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Locked, so no online database renames it or fills it in, and created with its members in one
            // go, so it is never empty and therefore never visible to users who may not see its library.
            await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = creation.Studio,
                IsLocked = true,
                ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [ProviderKey] = creation.Studio },
                ItemIdList = creation.Members.Select(id => id.ToString("N")).ToList(),
            }).ConfigureAwait(false);
        }
    }

    private void QueueMissingImages(List<Movie> movies)
    {
        // A locked collection only gets images on a full image refresh; StudioCollectionImageProvider then
        // gives it the cover of its newest video. Only asked for while there is a cover to give.
        var withCover = movies.Where(StudioCollectionImageProvider.HasLocalCover).Select(m => m.Id).ToHashSet();
        foreach (var collection in Existing())
        {
            if (collection.HasImage(ImageType.Primary)
                || !collection.LinkedChildren.Any(c => c.ItemId is { } id && withCover.Contains(id)))
            {
                continue;
            }

            _providerManager.QueueRefresh(
                collection.Id,
                new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                {
                    MetadataRefreshMode = MetadataRefreshMode.Default,
                    ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                },
                RefreshPriority.Low);
        }
    }
}
