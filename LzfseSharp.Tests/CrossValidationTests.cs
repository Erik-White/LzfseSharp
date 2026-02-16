using System.Text;
using AwesomeAssertions;
using Lzfse;

namespace LzfseSharp.Tests;

/// <summary>
/// Cross-validation tests that verify LzfseSharp produces identical output
/// to the reference lzfse-net implementation (which wraps the native C library)
/// </summary>
public class CrossValidationTests
{
    [Fact]
    public void Decompress_SimpleString_MatchesReference()
    {
        string testData = "Hello, LZFSE! This is a test string for compression.";
        byte[] original = Encoding.UTF8.GetBytes(testData);
        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
        Encoding.UTF8.GetString(decompressed).Should().Be(testData);
    }

    [Fact]
    public void Decompress_EmptyData_MatchesReference()
    {
        byte[] original = [];
        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[100];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(0);
    }

    [Fact]
    public void Decompress_SingleByte_MatchesReference()
    {
        byte[] original = [42];
        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
    }

    [Fact]
    public void Decompress_RepeatingPattern_MatchesReference()
    {
        byte[] original = new byte[1000];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i % 10);

        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
    }

    [Fact]
    public void Decompress_RandomData_MatchesReference()
    {
        var random = new Random(42);
        byte[] original = new byte[500];
        random.NextBytes(original);

        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
    }

    [Fact]
    public void Decompress_LargeRepeatingData_MatchesReference()
    {
        byte[] original = new byte[10000];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i / 100 % 256);

        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
    }

    [Fact]
    public void Decompress_AllZeros_MatchesReference()
    {
        byte[] original = new byte[1000];
        Array.Fill(original, (byte)0);

        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
    }

    [Fact]
    public void Decompress_TextContent_MatchesReference()
    {
        string text = @"
        Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor
        incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis
        nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.
        Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore
        eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt
        in culpa qui officia deserunt mollit anim id est laborum.

        The quick brown fox jumps over the lazy dog. The five boxing wizards jump quickly.
        Pack my box with five dozen liquor jugs. How vexingly quick daft zebras jump!
        ";
        byte[] original = Encoding.UTF8.GetBytes(text);
        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
        Encoding.UTF8.GetString(decompressed).Should().Be(text);
    }

    [Fact]
    public void Decompress_BinaryData_MatchesReference()
    {
        byte[] original = new byte[2000];
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(
                (i * 17) ^
                ((i >> 3) & 0xFF) ^
                ((i & 0x0F) << 4)
            );
        }

        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
    }

    [Fact]
    public void Decompress_MultipleBlocks_MatchesReference()
    {
        byte[] original = new byte[50000];
        var random = new Random(123);
        for (int i = 0; i < original.Length; i += 100)
        {
            byte pattern = (byte)random.Next(256);
            for (int j = 0; j < 100 && i + j < original.Length; j++)
                original[i + j] = pattern;
        }

        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(10000)]
    public void Decompress_VariousSizes_MatchesReference(int size)
    {
        byte[] original = new byte[size];
        for (int i = 0; i < size; i++)
            original[i] = (byte)(i % 256);

        byte[] compressed = CompressWithReference(original);

        byte[] decompressed = new byte[size];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

        bytesWritten.Should().Be(size);
        decompressed.Should().Equal(original);
    }

    /// <summary>
    /// Helper to compress data using reference implementation
    /// </summary>
    private static byte[] CompressWithReference(byte[] data)
    {
        // Allocate generous buffer for compressed output
        byte[] compressedBuffer = new byte[data.Length * 2 + 1024];
        int compressedSize = LzfseCompressor.Compress(data, compressedBuffer);

        // Return only the used portion
        byte[] result = new byte[compressedSize];
        Array.Copy(compressedBuffer, result, compressedSize);

        return result;
    }
}
