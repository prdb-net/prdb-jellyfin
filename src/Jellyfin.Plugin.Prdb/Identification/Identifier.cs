using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Prdb.Api;
using Jellyfin.Plugin.Prdb.Configuration;
using Jellyfin.Plugin.Prdb.Hashing;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// Identifies files against prdb and decides, by the administrator's switches, which matches are applied.
/// Knows nothing of Jellyfin's library: the caller hands it files and applies what it decides.
/// </summary>
public sealed class Identifier
{
    /// <summary>How many files go into one identify request; prdb's maximum.</summary>
    internal const int BatchSize = PrdbClient.MaxIdentifyBatchSize;

    private readonly IPrdbClient _client;
    private readonly IdentificationStore _store;
    private readonly VideoCache _cache;
    private readonly ILogger<Identifier> _logger;
    private readonly Func<string, string?> _hash;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="Identifier"/> class.</summary>
    /// <param name="client">The prdb client.</param>
    /// <param name="store">The remembered answers.</param>
    /// <param name="cache">The video cache.</param>
    /// <param name="logger">The logger.</param>
    public Identifier(IPrdbClient client, IdentificationStore store, VideoCache cache, ILogger<Identifier> logger)
        : this(client, store, cache, logger, OsHash.TryCompute, TimeProvider.System)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Identifier"/> class with replaceable hashing and time, for tests.</summary>
    /// <param name="client">The prdb client.</param>
    /// <param name="store">The remembered answers.</param>
    /// <param name="cache">The video cache.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="hash">Computes a file's OSHash.</param>
    /// <param name="time">The clock.</param>
    internal Identifier(IPrdbClient client, IdentificationStore store, VideoCache cache, ILogger<Identifier> logger, Func<string, string?> hash, TimeProvider time)
    {
        _client = client;
        _store = store;
        _cache = cache;
        _logger = logger;
        _hash = hash;
        _time = time;
    }

    /// <summary>
    /// Identifies a batch of library files and hands every match the switches allow to
    /// <paramref name="apply"/>. Answers that apply nothing are remembered, so the files are not asked
    /// about again until they change, the switches change or the answer grows stale.
    /// </summary>
    /// <param name="connection">Where and as whom to ask.</param>
    /// <param name="configuration">The administrator's switches.</param>
    /// <param name="candidates">The files to identify.</param>
    /// <param name="apply">Applies a match: the candidate and the video id.</param>
    /// <param name="recheckStale">Whether answers older than <see cref="IdentificationStore.RecheckAfter"/> are asked again.</param>
    /// <param name="progress">Progress from 0 to 100.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the run did.</returns>
    public async Task<IdentificationSummary> IdentifyAsync(
        PrdbConnection connection,
        PluginConfiguration configuration,
        IReadOnlyList<IdentificationCandidate> candidates,
        Func<IdentificationCandidate, Guid, CancellationToken, Task> apply,
        bool recheckStale,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(apply);

        var summary = new IdentificationSummary();
        var switches = MatchPolicy.SwitchesKey(configuration);
        var now = _time.GetUtcNow();

        var due = new List<(IdentificationCandidate Candidate, FileFingerprint File)>();
        foreach (var candidate in candidates)
        {
            var file = FileFingerprint.Describe(candidate.Path);
            if (file is null)
            {
                continue;
            }

            if (!_store.NeedsCheck(candidate.Path, file, switches, now, recheckStale))
            {
                summary.Skipped++;
                continue;
            }

            due.Add((candidate, file));
        }

        try
        {
            var done = 0;
            foreach (var batch in due.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Hash only the files of the batch about to be sent, so a cancelled run has read no more than it used.
                var files = batch.Select(entry => entry with { File = WithHash(entry.Candidate.Path, entry.File) }).ToList();
                var request = files
                    .Select((entry, index) => new IdentifyFile(index.ToString(CultureInfo.InvariantCulture), entry.File.Filename, entry.File.Filesize, entry.File.OsHash))
                    .ToList();

                IReadOnlyList<IdentifyResult> results;
                try
                {
                    results = await _client.IdentifyAsync(connection, request, includeVideoDetails: true, cancellationToken).ConfigureAwait(false);
                }
                catch (PrdbApiException ex)
                {
                    // Whatever made prdb fail this batch (a rejected key, a used-up budget, an outage) fails
                    // the next one too; stop and let the next run pick the files up.
                    summary.Failed += due.Count - done;
                    _logger.LogError("Stopped identifying against prdb: {Reason}", ex.Message);
                    return summary;
                }

                for (var i = 0; i < files.Count; i++)
                {
                    await HandleAsync(files[i].Candidate, files[i].File, results[i], configuration, switches, apply, summary, cancellationToken).ConfigureAwait(false);
                }

                done += files.Count;
                progress?.Report(100.0 * done / due.Count);
            }
        }
        finally
        {
            _store.Save();
        }

        progress?.Report(100);
        return summary;
    }

    /// <summary>
    /// Identifies one file and returns the video to apply, or <c>null</c> when nothing may be applied. Used
    /// when Jellyfin refreshes a single item that has no prdb id yet.
    /// </summary>
    /// <param name="connection">Where and as whom to ask.</param>
    /// <param name="configuration">The administrator's switches.</param>
    /// <param name="path">The full path of the file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The video, or <c>null</c>.</returns>
    public async Task<VideoDetail?> IdentifyOneAsync(PrdbConnection connection, PluginConfiguration configuration, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var file = FileFingerprint.Describe(path);
        if (file is null)
        {
            return null;
        }

        var switches = MatchPolicy.SwitchesKey(configuration);
        if (!_store.NeedsCheck(path, file, switches, _time.GetUtcNow(), recheckStale: true))
        {
            return null;
        }

        file = WithHash(path, file);
        var results = await _client.IdentifyAsync(
            connection,
            [new IdentifyFile("0", file.Filename, file.Filesize, file.OsHash)],
            includeVideoDetails: true,
            cancellationToken).ConfigureAwait(false);

        var result = results[0];
        var decision = MatchPolicy.Decide(result, configuration);
        LogDecision(path, result, decision);
        if (decision != MatchDecision.Apply)
        {
            _store.Remember(path, file, switches, decision, _time.GetUtcNow());
            _store.Save();
            return null;
        }

        _store.Forget(path);
        _store.Save();
        var video = result.Video ?? await _client.GetVideoAsync(connection, result.VideoId!.Value, cancellationToken).ConfigureAwait(false);
        if (video is not null)
        {
            _cache.Put(video);
        }

        return video;
    }

    private async Task HandleAsync(
        IdentificationCandidate candidate,
        FileFingerprint file,
        IdentifyResult result,
        PluginConfiguration configuration,
        string switches,
        Func<IdentificationCandidate, Guid, CancellationToken, Task> apply,
        IdentificationSummary summary,
        CancellationToken cancellationToken)
    {
        var decision = MatchPolicy.Decide(result, configuration);
        summary.Count(result, decision);
        LogDecision(candidate.Path, result, decision);

        if (decision != MatchDecision.Apply)
        {
            _store.Remember(candidate.Path, file, switches, decision, _time.GetUtcNow());
            return;
        }

        if (result.Video is not null)
        {
            _cache.Put(result.Video);
        }

        _store.Forget(candidate.Path);
        await apply(candidate, result.VideoId!.Value, cancellationToken).ConfigureAwait(false);
    }

    private FileFingerprint WithHash(string path, FileFingerprint file) =>
        file with { OsHash = _store.KnownHash(path, file) ?? _hash(path) };

    private void LogDecision(string path, IdentifyResult result, MatchDecision decision)
    {
        if (decision == MatchDecision.Apply)
        {
            _logger.LogInformation("prdb identified {Path} as video {VideoId} ({Confidence} by {MatchedBy})", path, result.VideoId, result.Confidence, result.MatchedBy);
        }
        else
        {
            _logger.LogDebug("prdb: {Decision} for {Path} ({Confidence}, {CandidateCount} candidates)", decision, path, result.Confidence, result.Candidates?.Count ?? 0);
        }
    }
}
