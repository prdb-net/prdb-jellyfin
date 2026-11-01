using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Prdb.Api;

/// <summary>
/// How far an identification can be trusted, as <c>POST /videos/identify</c> reports it. The API sends the
/// numeric value; a value this version does not know deserializes to a number outside the named members
/// and is treated like <see cref="None"/>.
/// </summary>
public enum IdentifyConfidence
{
    /// <summary>No match at all.</summary>
    None = 0,

    /// <summary>Only the site could be attributed; there is no video.</summary>
    Partial = 1,

    /// <summary>Matched on a name, which can collide.</summary>
    Probable = 2,

    /// <summary>Matched on a stored file name for this exact file.</summary>
    Strong = 3,

    /// <summary>Matched on a file hash.</summary>
    Exact = 4,

    /// <summary>Several videos matched equally well.</summary>
    Ambiguous = 5,
}

/// <summary>Which rung of prdb's identification ladder produced a match.</summary>
public enum IdentifyMatchKind
{
    /// <summary>An exact OS hash match.</summary>
    OsHash = 0,

    /// <summary>An exact perceptual hash match.</summary>
    PHash = 1,

    /// <summary>An exact match on a stored file name for this file.</summary>
    Filename = 2,

    /// <summary>The file name without its extension equals a scene release title.</summary>
    ReleaseName = 3,

    /// <summary>Only the site could be read out of the file name.</summary>
    Site = 4,

    /// <summary>An exact MD5 match.</summary>
    Md5 = 5,

    /// <summary>The file name carries the site, the release date and the whole title of one video.</summary>
    SiteDateTitle = 6,
}

/// <summary>The image types of an actor image.</summary>
public enum ActorImageType
{
    /// <summary>Not a type the API sends; the value a missing field deserializes to.</summary>
    Unknown = 0,

    /// <summary>A small thumbnail.</summary>
    Thumbnail = 1,

    /// <summary>A poster.</summary>
    Poster = 2,

    /// <summary>A face crop.</summary>
    Face = 3,
}

/// <summary>One file to identify.</summary>
/// <param name="Ref">The caller's identifier, returned unchanged; unique within a request.</param>
/// <param name="Filename">The file name without directory.</param>
/// <param name="Filesize">The size in bytes, if known.</param>
/// <param name="OsHash">The OS hash, if computed.</param>
public sealed record IdentifyFile(string Ref, string Filename, long? Filesize, string? OsHash);

/// <summary>The request body of <c>POST /videos/identify</c>.</summary>
/// <param name="Files">Between 1 and 200 files.</param>
/// <param name="IncludeVideoDetails">Whether each unique match carries the full video document.</param>
internal sealed record IdentifyRequest(IReadOnlyList<IdentifyFile> Files, bool IncludeVideoDetails);

/// <summary>The response body of <c>POST /videos/identify</c>.</summary>
/// <param name="Results">One result per input file, in input order.</param>
internal sealed record IdentifyResponse(IReadOnlyList<IdentifyResult> Results);

/// <summary>What prdb made of one file.</summary>
/// <param name="Ref">The caller's identifier of the file.</param>
/// <param name="Confidence">How far the result can be trusted.</param>
/// <param name="MatchedBy">The rung that matched, or <c>null</c>.</param>
/// <param name="VideoId">The video, when exactly one was found.</param>
/// <param name="Video">The full video document, when requested and exactly one was found.</param>
/// <param name="Candidates">All equally good videos when the match was ambiguous.</param>
public sealed record IdentifyResult(
    string Ref,
    IdentifyConfidence Confidence,
    IdentifyMatchKind? MatchedBy,
    Guid? VideoId,
    VideoDetail? Video,
    IReadOnlyList<Guid>? Candidates);

/// <summary>A video as <c>GET /videos/{id}</c> returns it, reduced to what the plugin maps.</summary>
/// <param name="Id">The video id.</param>
/// <param name="Title">The title.</param>
/// <param name="Description">The catalogue description, if known.</param>
/// <param name="StashdbSceneId">The StashDB scene id of a confirmed match, if any.</param>
/// <param name="ReleaseDate">The release date, if known.</param>
/// <param name="Site">The site that published the video.</param>
/// <param name="Images">The video's images, oldest first.</param>
/// <param name="Actors">The performers.</param>
public sealed record VideoDetail(
    Guid Id,
    string Title,
    string? Description,
    Guid? StashdbSceneId,
    DateOnly? ReleaseDate,
    VideoSite? Site,
    IReadOnlyList<VideoImage>? Images,
    IReadOnlyList<VideoActor>? Actors);

/// <summary>The site of a video.</summary>
/// <param name="Id">The site id.</param>
/// <param name="Title">The site title.</param>
/// <param name="Url">The site's URL.</param>
/// <param name="Network">The network the site belongs to, if any.</param>
public sealed record VideoSite(Guid Id, string Title, string? Url, VideoNetwork? Network);

/// <summary>The network of a site.</summary>
/// <param name="Id">The network id.</param>
/// <param name="Title">The network title.</param>
public sealed record VideoNetwork(Guid Id, string Title);

/// <summary>An image of a video.</summary>
/// <param name="Id">The image id.</param>
/// <param name="Url">The absolute URL, if available.</param>
public sealed record VideoImage(Guid Id, string? Url);

/// <summary>A performer of a video.</summary>
/// <param name="Id">The actor id.</param>
/// <param name="Name">The stage name.</param>
/// <param name="Images">The actor's images.</param>
public sealed record VideoActor(Guid Id, string Name, IReadOnlyList<ActorImage>? Images);

/// <summary>An image of an actor.</summary>
/// <param name="Id">The image id.</param>
/// <param name="Url">The absolute URL, if available.</param>
/// <param name="ImageType">What kind of image it is.</param>
public sealed record ActorImage(Guid Id, string? Url, ActorImageType ImageType);

/// <summary>An actor as <c>GET /actors/{id}</c> returns it, reduced to what the plugin maps.</summary>
/// <param name="Id">The actor id.</param>
/// <param name="Name">The stage name.</param>
/// <param name="Birthday">The date of birth, if known.</param>
/// <param name="Deathday">The date of death, if applicable.</param>
/// <param name="Birthplace">The birthplace, if known.</param>
/// <param name="NationalityLabel">The nationality as text.</param>
/// <param name="Height">The height in centimetres, if known.</param>
/// <param name="Images">The actor's images.</param>
/// <param name="Bios">Biographies.</param>
public sealed record ActorDetail(
    Guid Id,
    string Name,
    DateOnly? Birthday,
    DateOnly? Deathday,
    string? Birthplace,
    string? NationalityLabel,
    int? Height,
    IReadOnlyList<ActorImage>? Images,
    IReadOnlyList<ActorBio>? Bios);

/// <summary>A biography of an actor.</summary>
/// <param name="Id">The biography id.</param>
/// <param name="Text">The text.</param>
public sealed record ActorBio(Guid Id, string Text);

/// <summary>The account's request budget, as <c>GET /rate-limit</c> returns it.</summary>
/// <param name="Hourly">The hourly window.</param>
/// <param name="Monthly">The monthly window.</param>
public sealed record RateLimitStatus(RateLimitWindow Hourly, RateLimitWindow Monthly);

/// <summary>One rate-limit window.</summary>
/// <param name="Limit">The budget of the window.</param>
/// <param name="Used">What is used of it.</param>
/// <param name="Remaining">What is left of it.</param>
public sealed record RateLimitWindow(int Limit, int Used, int Remaining);
