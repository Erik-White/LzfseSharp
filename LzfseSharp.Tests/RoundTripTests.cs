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
