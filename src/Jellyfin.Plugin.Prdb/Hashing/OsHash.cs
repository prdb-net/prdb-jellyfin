using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;

namespace Jellyfin.Plugin.Prdb.Hashing;

/// <summary>
/// The OpenSubtitles hash prdb keys file hashes by: the file size plus the sum of the 64-bit little-endian
/// words of the first and the last 64 KiB, overflow discarded, as 16 lowercase hexadecimal characters.
/// </summary>
/// <remarks>
/// Only 128 KiB of a file are read however large it is, which is what makes it cheap enough to compute
/// during a library scan. A file shorter than two chunks gets no hash rather than one computed over
/// overlapping or padded chunks: a value no other client produces would match nothing, and the file is
/// still identified by its name.
/// </remarks>
internal static class OsHash
{
    /// <summary>The size of each of the two chunks that are read.</summary>
    internal const int ChunkSize = 64 * 1024;

    /// <summary>
    /// Computes the hash of the file at <paramref name="path"/>, or returns <c>null</c> when the file is too
    /// small, missing or unreadable.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns>The hash, or <c>null</c>.</returns>
    public static string? TryCompute(string path)
    {
        try
        {
            // ReadWrite and Delete sharing so a file that is still being copied or moved is not disturbed.
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.RandomAccess);
            return Compute(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Computes the hash of a seekable stream, or returns <c>null</c> when it is shorter than two chunks.
    /// </summary>
    /// <param name="stream">A readable, seekable stream positioned anywhere.</param>
    /// <returns>The hash, or <c>null</c>.</returns>
    public static string? Compute(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var length = stream.Length;
        if (length < 2L * ChunkSize)
        {
            return null;
        }

        var buffer = new byte[ChunkSize];
        var hash = unchecked((ulong)length);

        stream.Position = 0;
        stream.ReadExactly(buffer);
        hash = unchecked(hash + SumWords(buffer));

        stream.Position = length - ChunkSize;
        stream.ReadExactly(buffer);
        hash = unchecked(hash + SumWords(buffer));

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static ulong SumWords(ReadOnlySpan<byte> chunk)
    {
        ulong sum = 0;
        for (var offset = 0; offset < chunk.Length; offset += sizeof(ulong))
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt64LittleEndian(chunk.Slice(offset, sizeof(ulong))));
        }

        return sum;
    }
}
