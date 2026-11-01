using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Prdb.Api;

/// <summary>
/// The parts of the prdb public API the plugin uses. Every call is read-only.
/// </summary>
public interface IPrdbClient
{
    /// <summary>
    /// Identifies files against the catalogue. Any number of files may be passed; they are sent in batches of
    /// at most <see cref="PrdbClient.MaxIdentifyBatchSize"/>.
    /// </summary>
    /// <param name="connection">Where and as whom to ask.</param>
    /// <param name="files">The files; each <see cref="IdentifyFile.Ref"/> must be unique.</param>
    /// <param name="includeVideoDetails">Whether unique matches carry the full video document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One result per file, in input order.</returns>
    Task<IReadOnlyList<IdentifyResult>> IdentifyAsync(
        PrdbConnection connection,
        IReadOnlyList<IdentifyFile> files,
        bool includeVideoDetails,
        CancellationToken cancellationToken);

    /// <summary>Gets a video, or <c>null</c> when prdb does not know the id.</summary>
    /// <param name="connection">Where and as whom to ask.</param>
    /// <param name="videoId">The video id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The video, or <c>null</c>.</returns>
    Task<VideoDetail?> GetVideoAsync(PrdbConnection connection, Guid videoId, CancellationToken cancellationToken);

    /// <summary>Gets an actor, or <c>null</c> when prdb does not know the id.</summary>
    /// <param name="connection">Where and as whom to ask.</param>
    /// <param name="actorId">The actor id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The actor, or <c>null</c>.</returns>
    Task<ActorDetail?> GetActorAsync(PrdbConnection connection, Guid actorId, CancellationToken cancellationToken);

    /// <summary>Gets the account's request budget; used to check a key.</summary>
    /// <param name="connection">Where and as whom to ask.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The budget.</returns>
    Task<RateLimitStatus> GetRateLimitAsync(PrdbConnection connection, CancellationToken cancellationToken);
}
