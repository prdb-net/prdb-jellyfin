using System;
using System.IO;

namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// What the plugin sends prdb about one file: its name without the folder, its size and its OSHash.
/// The modification time is kept locally only, to tell whether a stored hash still belongs to the file.
/// </summary>
/// <param name="Filename">The file name without directory.</param>
/// <param name="Filesize">The size in bytes.</param>
/// <param name="LastWriteTimeUtc">The modification time.</param>
/// <param name="OsHash">The OSHash, or <c>null</c> when none could be computed.</param>
public sealed record FileFingerprint(string Filename, long Filesize, DateTime LastWriteTimeUtc, string? OsHash)
{
    /// <summary>
    /// Reads name, size and modification time of a file without hashing it, or returns <c>null</c> when
    /// the file does not exist.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <returns>The fingerprint without hash, or <c>null</c>.</returns>
    public static FileFingerprint? Describe(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        return info.Exists ? new FileFingerprint(info.Name, info.Length, info.LastWriteTimeUtc, null) : null;
    }

    /// <summary>Gets a value indicating whether another fingerprint describes the same file content and name.</summary>
    /// <param name="other">The other fingerprint.</param>
    /// <returns><c>true</c> when name, size and modification time agree.</returns>
    public bool SameFileAs(FileFingerprint? other) =>
        other is not null
        && string.Equals(Filename, other.Filename, StringComparison.Ordinal)
        && Filesize == other.Filesize
        && LastWriteTimeUtc == other.LastWriteTimeUtc;
}
