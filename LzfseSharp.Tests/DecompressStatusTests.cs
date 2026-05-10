using AwesomeAssertions;
using LzfseSharp.Core;
using Lzfse;
using Xunit;

namespace LzfseSharp.Tests;

/// <summary>
/// Verifies that each <see cref="DecompressStatus"/> value is returned by the
/// out-param overload of <see cref="LzfseDecoder.Decompress(Span{byte}, ReadOnlySpan{byte}, out DecompressStatus)"/>
/// for the scenario it names. Maps to the LZFSE_STATUS_* outcomes documented in
/// the reference implementation's src/lzfse.h.
/// </summary>
public class DecompressStatusTests
{
    [Fact]
    public void Status_Ok_OnReferenceRoundTrip()
    {
        byte[] original = "The quick brown fox jumps over the lazy dog."u8.ToArray();
        byte[] compressedBuffer = new byte[original.Length * 2 + 1024];
        int size = LzfseCompressor.Compress(original, compressedBuffer);

        byte[] dst = new byte[original.Length];
        int written = LzfseDecoder.Decompress(dst, compressedBuffer.AsSpan(0, size).ToArray(), out DecompressStatus status);

        status.Should().Be(DecompressStatus.Ok);
        written.Should().Be(original.Length);
    }

    [Fact]
    public void Status_SourceTruncated_WhenEmptyInput()
    {
        byte[] dst = new byte[16];
        int written = LzfseDecoder.Decompress(dst, ReadOnlySpan<byte>.Empty, out DecompressStatus status);

        status.Should().Be(DecompressStatus.SourceTruncated);
        written.Should().Be(0);
    }

    [Fact]
    public void Status_SourceTruncated_WhenBlockMagicIncomplete()
    {
        byte[] stream = [0x62, 0x76, 0x78]; // 3 of 4 magic bytes
        byte[] dst = new byte[16];
        LzfseDecoder.Decompress(dst, stream, out DecompressStatus status);

        status.Should().Be(DecompressStatus.SourceTruncated);
    }

    [Fact]
    public void Status_SourceTruncated_WhenUncompressedPayloadShort()
    {
        // bvx- header claims 10 bytes of raw data but only 3 are present.
        byte[] stream = new byte[8 + 3];
        MemoryOperations.Store4(stream, Constants.UncompressedBlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 10);
        stream[8] = (byte)'x';
        stream[9] = (byte)'y';
        stream[10] = (byte)'z';

        byte[] dst = new byte[16];
        int written = LzfseDecoder.Decompress(dst, stream, out DecompressStatus status);

        status.Should().Be(DecompressStatus.SourceTruncated);
        written.Should().Be(3, "partial payload should still be exposed to the caller");
    }

    [Fact]
    public void Status_DestinationFull_WhenBufferTooSmall()
    {
        // Same stream as the existing Decompress_DestinationTooSmall test: "Hello" in a
        // bvx- block, but dst is only 3 bytes.
        byte[] stream =
        [
            0x62, 0x76, 0x78, 0x2d,
            0x05, 0x00, 0x00, 0x00,
            0x48, 0x65, 0x6c, 0x6c, 0x6f,
            0x62, 0x76, 0x78, 0x24,
        ];
        byte[] dst = new byte[3];
        int written = LzfseDecoder.Decompress(dst, stream, out DecompressStatus status);

        status.Should().Be(DecompressStatus.DestinationFull);
        written.Should().Be(3, "dst should be filled up to the point of exhaustion");
        dst.Should().Equal((byte)'H', (byte)'e', (byte)'l');
    }

    [Fact]
    public void Status_Malformed_WhenBlockMagicUnknown()
    {
        byte[] stream = [0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0];
        byte[] dst = new byte[16];
        LzfseDecoder.Decompress(dst, stream, out DecompressStatus status);

        status.Should().Be(DecompressStatus.Malformed);
    }

    [Fact]
    public void LegacyOverload_StillThrowsOnDestinationFull()
    {
        byte[] stream =
        [
            0x62, 0x76, 0x78, 0x2d,
            0x05, 0x00, 0x00, 0x00,
            0x48, 0x65, 0x6c, 0x6c, 0x6f,
            0x62, 0x76, 0x78, 0x24,
        ];
        byte[] dst = new byte[3];

        var ex = Assert.Throws<ArgumentException>(() => LzfseDecoder.Decompress(dst, stream));
        ex.ParamName.Should().Be("dstBuffer");
    }
}
