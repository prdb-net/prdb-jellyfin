using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Prdb.Identification;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Prdb.ScheduledTasks;

/// <summary>
/// The scheduled task <c>prdb: identify library</c>: identifies every movie without a prdb id, including
/// those whose last answer has grown stale.
/// </summary>
/// <param name="libraryIdentifier">The library identifier.</param>
public class IdentifyLibraryTask(LibraryIdentifier libraryIdentifier) : IScheduledTask
{
    /// <inheritdoc />
    public string Name => "Identify library";

    /// <inheritdoc />
    public string Key => "PrdbIdentifyLibrary";

    /// <inheritdoc />
    public string Description => "Identifies movies without a prdb id by file hash and file name, and applies the matches the plugin settings allow.";

    /// <inheritdoc />
    public string Category => "prdb";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) =>
        libraryIdentifier.RunAsync(recheckStale: true, progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks,
        },
    ];
}
