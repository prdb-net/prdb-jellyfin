using System;

namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// A library item without a prdb id whose file is to be identified.
/// </summary>
/// <param name="ItemId">The Jellyfin item.</param>
/// <param name="Path">The full path of its file.</param>
public sealed record IdentificationCandidate(Guid ItemId, string Path);
