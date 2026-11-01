using Jellyfin.Plugin.Prdb.Hashing;

namespace Jellyfin.Plugin.Prdb.Tests.Hashing;

public class OsHashTests
{
    [Fact]
    public void A_file_of_zeros_hashes_to_its_size()
    {
        using var stream = new MemoryStream(new byte[3 * OsHash.ChunkSize]);

        Assert.Equal((3L * OsHash.ChunkSize).ToString("x16"), OsHash.Compute(stream));
    }

    [Theory]
    [InlineData(2 * OsHash.ChunkSize)]
    [InlineData((2 * OsHash.ChunkSize) + 1)]
    [InlineData((2 * OsHash.ChunkSize) + 7)]
    [InlineData(5_000_003)]
    public void The_hash_matches_a_straightforward_reference(int size)
    {
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        using var stream = new MemoryStream(bytes);

        Assert.Equal(Reference(bytes), OsHash.Compute(stream));
    }

    [Fact]
    public void Exactly_two_chunks_read_every_byte_once()
    {
        // With exactly two chunks the head and the tail meet without overlapping, so the hash is the size
        // plus the sum over the whole content.
        var bytes = new byte[2 * OsHash.ChunkSize];
        bytes[0] = 1;
        bytes[^8] = 2;
        using var stream = new MemoryStream(bytes);

        Assert.Equal(((ulong)bytes.Length + 1 + 2).ToString("x16"), OsHash.Compute(stream));
    }

    [Fact]
    public void Overflow_wraps_instead_of_failing()
    {
        var bytes = Enumerable.Repeat((byte)0xFF, 2 * OsHash.ChunkSize).ToArray();
        using var stream = new MemoryStream(bytes);

        Assert.Equal(Reference(bytes), OsHash.Compute(stream));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData((2 * OsHash.ChunkSize) - 1)]
    public void A_file_shorter_than_two_chunks_gets_no_hash(int size)
    {
        using var stream = new MemoryStream(new byte[size]);

        Assert.Null(OsHash.Compute(stream));
    }

    [Fact]
    public void A_missing_file_gets_no_hash()
    {
        Assert.Null(OsHash.TryCompute(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void A_file_on_disk_hashes_like_its_stream()
    {
        var bytes = new byte[300_000];
        new Random(42).NextBytes(bytes);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);

            Assert.Equal(Reference(bytes), OsHash.TryCompute(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_hash_is_lowercase_hexadecimal_of_sixteen_characters()
    {
        var bytes = new byte[2 * OsHash.ChunkSize];
        new Random(7).NextBytes(bytes);
        using var stream = new MemoryStream(bytes);

        var hash = OsHash.Compute(stream);

        Assert.Matches("^[0-9a-f]{16}$", hash);
    }

    private static string Reference(byte[] bytes)
    {
        ulong hash = (ulong)bytes.Length;
        for (var i = 0; i < 65536; i += 8)
        {
            hash = unchecked(hash + BitConverter.ToUInt64(bytes, i));
        }

        for (var i = bytes.Length - 65536; i < bytes.Length; i += 8)
        {
            hash = unchecked(hash + BitConverter.ToUInt64(bytes, i));
        }

        return hash.ToString("x16");
    }
}
