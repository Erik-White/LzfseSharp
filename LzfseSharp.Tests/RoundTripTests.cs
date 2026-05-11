using AwesomeAssertions;
using Lzfse;
using Xunit;

namespace LzfseSharp.Tests;

public class RoundTripTests
{
    private static void AssertRoundTrip(byte[] original)
    {
        // Compress with reference implementation
        byte[] compressedBuffer = new byte[original.Length * 2 + 1024];
        int compressedSize = LzfseCompressor.Compress(original, compressedBuffer);
        compressedSize.Should().BeGreaterThan(0, "Compression failed");

        byte[] compressed = new byte[compressedSize];
        Array.Copy(compressedBuffer, compressed, compressedSize);

        // Decompress with our implementation
        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);

        // The allocating overload must produce the same bytes with no caller sizing.
        byte[] allocated = LzfseDecoder.Decompress(compressed);
        allocated.Should().Equal(original);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(10000)]
    [InlineData(50000)]
    [InlineData(100000)]
    public void TestSyntheticData(int size)
    {
        byte[] original = new byte[size];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i % 10);

        AssertRoundTrip(original);
    }

    [Theory]
    [InlineData(500_000)]
    [InlineData(1_000_000)]
    [InlineData(4_000_000)]
    public void TestLargeRepeatingData(int size)
    {
        // Exercises multi-block transitions at scale: the reference encoder packs
        // ~40 KB per LZFSE block, so 4 MB forces ~100 block-boundary traversals.
        byte[] original = new byte[size];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i % 251);

        AssertRoundTrip(original);
    }

    [Theory]
    [InlineData(500_000)]
    [InlineData(1_000_000)]
    public void TestLargeMixedData(int size)
    {
        // Mix of repeating patterns, sparse randomness, and runs — forces the encoder
        // to pick different block types for different segments, stressing the outer
        // bvxn / bvx2 / bvx- transitions in LzfseDecoder.DecodeInternal.
        byte[] original = new byte[size];
        Random random = new Random(7);
        for (int i = 0; i < size; i++)
        {
            if (i % 1024 < 256) original[i] = (byte)(i & 0xff);          // linear run
            else if (i % 1024 < 512) original[i] = 0x5A;                  // constant run
            else if (i % 1024 < 768) original[i] = (byte)random.Next(256);// random bytes
            else original[i] = (byte)('A' + (i % 26));                    // text-like
        }

        AssertRoundTrip(original);
    }

    [Fact]
    public void TestRandomData()
    {
        var random = new Random(42);
        byte[] original = new byte[10000];
        random.NextBytes(original);

        AssertRoundTrip(original);
    }

    [Theory]
    [InlineData("CrossValidationTests.cs")]
    [InlineData("LzfseDecoderTests.cs")]
    [InlineData("RoundTripTests.cs")]
    public void TestSourceFile(string filename)
    {
        string projectRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..");
        string path = Path.Combine(projectRoot, filename);
        byte[] original = File.ReadAllBytes(path);

        AssertRoundTrip(original);
    }
}
