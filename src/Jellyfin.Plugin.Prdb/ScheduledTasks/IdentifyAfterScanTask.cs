using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Prdb.Identification;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Prdb.ScheduledTasks;

/// <summary>
/// Runs after every library scan and identifies the movies the scan added, so new files do not wait for
/// the scheduled task. Answers that only grew stale are left to the scheduled task.
/// </summary>
/// <param name="libraryIdentifier">The library identifier.</param>
public class IdentifyAfterScanTask(LibraryIdentifier libraryIdentifier) : ILibraryPostScanTask
{
    /// <inheritdoc />
    public Task Run(IProgress<double> progress, CancellationToken cancellationToken) =>
        libraryIdentifier.RunAsync(recheckStale: false, progress, cancellationToken);
}
