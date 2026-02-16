using System.Text;
using AwesomeAssertions;
using Xunit;

namespace LzfseSharp.Tests;

public class LzfseDecoderTests
{
    [Fact]
    public void Decompress_EmptyInput_ReturnsZero()
    {
        Span<byte> dst = new byte[100];
        ReadOnlySpan<byte> src = [];

        int result = LzfseDecoder.Decompress(dst, src);

        result.Should().Be(0);
    }

    [Fact]
    public void Decompress_EmptyOutput_ReturnsZero()
    {
        Span<byte> dst = [];
        ReadOnlySpan<byte> src = new byte[10];

        int result = LzfseDecoder.Decompress(dst, src);

        result.Should().Be(0);
    }

    [Fact]
    public void Decompress_EndOfStreamBlock_ReturnsZero()
    {
        Span<byte> dst = new byte[100];
        // End of stream magic: 0x62767824 (little-endian: 24 78 76 62 = "bvx$")
        ReadOnlySpan<byte> src = [0x62, 0x76, 0x78, 0x24];

        int result = LzfseDecoder.Decompress(dst, src);

        result.Should().Be(0);
    }

    [Fact]
    public void Decompress_UncompressedBlock_CopiesData()
    {
        Span<byte> dst = new byte[100];

        // Create uncompressed block:
        // Magic: 0x62767824 (little-endian: 2d 78 76 62 = "bvx-")
        // n_raw_bytes: 5 (little-endian: 05 00 00 00)
        // Data: "Hello"
        byte[] srcBytes = [
            0x62, 0x76, 0x78, 0x2d,  // Magic: bvx-
            0x05, 0x00, 0x00, 0x00,  // n_raw_bytes: 5
            0x48, 0x65, 0x6c, 0x6c, 0x6f,  // "Hello"
            0x62, 0x76, 0x78, 0x24   // End of stream magic
        ];
        ReadOnlySpan<byte> src = srcBytes;

        int result = LzfseDecoder.Decompress(dst, src);

        result.Should().Be(5);
        Encoding.ASCII.GetString(dst[..5]).Should().Be("Hello");
    }

    [Fact]
    public void Decompress_InvalidMagic_ReturnsZero()
    {
        Span<byte> dst = new byte[100];
        ReadOnlySpan<byte> src = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00];

        int result = LzfseDecoder.Decompress(dst, src);

        result.Should().Be(0);
    }

    [Fact]
    public void Decompress_TruncatedHeader_ReturnsZero()
    {
        Span<byte> dst = new byte[100];
        // Uncompressed block magic but missing n_raw_bytes field
        ReadOnlySpan<byte> src = [0x62, 0x76, 0x78, 0x2d];

        int result = LzfseDecoder.Decompress(dst, src);

        result.Should().Be(0);
    }
}
