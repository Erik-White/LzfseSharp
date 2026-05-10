using System.Text;
using AwesomeAssertions;
using LzfseSharp.Core;
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
    public void Decompress_DestinationTooSmall_ThrowsArgumentException()
    {
        byte[] dstBytes = new byte[3];
        byte[] srcBytes = [
            0x62, 0x76, 0x78, 0x2d,  // Magic: bvx-
            0x05, 0x00, 0x00, 0x00,  // n_raw_bytes: 5
            0x48, 0x65, 0x6c, 0x6c, 0x6f,  // "Hello"
            0x62, 0x76, 0x78, 0x24   // End of stream magic
        ];

        var ex = Assert.Throws<ArgumentException>(() => LzfseDecoder.Decompress(dstBytes, srcBytes));
        ex.ParamName.Should().Be("dstBuffer");
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

    [Fact]
    public void Decompress_LzvnSmallLiteralFollowedByUncompressedBlock_DoesNotCorruptOutput()
    {
        // Regression guard for the CopyLiteralBytes fast path: when L <= 3 and
        // destinationLength >= 4, it stores 4 bytes and advances by L, leaving
        // 4-L "overshoot" bytes in dst past the logical write position. The outer
        // framer's DestinationEnd clamp should prevent those bytes from leaking
        // into the logical output of a subsequent block. This test verifies that
        // a small-literal LZVN block followed by an uncompressed block produces
        // exactly the expected bytes, with no corruption from the overshoot.

        // Block 1 (LZVN): small-literal opcode 0xe2 (L=2) + "AB" + 8-byte EOS
        byte[] lzvnPayload = new byte[1 + 2 + 8];
        lzvnPayload[0] = 0xe2;
        lzvnPayload[1] = (byte)'A';
        lzvnPayload[2] = (byte)'B';
        lzvnPayload[3] = 0x06; // EOS opcode

        byte[] stream = new byte[12 + lzvnPayload.Length + 8 + 2 + 4];
        int p = 0;

        MemoryOperations.Store4(stream.AsSpan(p), Constants.CompressedLzvnBlockMagic); p += 4;
        MemoryOperations.Store4(stream.AsSpan(p), 2); p += 4;                            // raw = 2
        MemoryOperations.Store4(stream.AsSpan(p), (uint)lzvnPayload.Length); p += 4;     // payload size
        lzvnPayload.CopyTo(stream, p); p += lzvnPayload.Length;

        // Block 2 (uncompressed): 2 bytes "YZ"
        MemoryOperations.Store4(stream.AsSpan(p), Constants.UncompressedBlockMagic); p += 4;
        MemoryOperations.Store4(stream.AsSpan(p), 2); p += 4;
        stream[p++] = (byte)'Y';
        stream[p++] = (byte)'Z';

        // End of stream
        MemoryOperations.Store4(stream.AsSpan(p), Constants.EndOfStreamBlockMagic);

        byte[] dst = new byte[4];
        int result = LzfseDecoder.Decompress(dst, stream);

        result.Should().Be(4);
        dst.Should().Equal((byte)'A', (byte)'B', (byte)'Y', (byte)'Z');
    }

    [Theory]
    [InlineData(28)] // bvx2 magic claims a header but fewer than 32 bytes of fixed-header data follow
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public void Decompress_V2BlockWithIncompleteFixedHeader_ReportsTruncated(int totalLength)
    {
        byte[] stream = new byte[totalLength];
        MemoryOperations.Store4(stream, Constants.CompressedV2BlockMagic);
        // Remaining bytes are zeros — their contents don't matter; the outer bounds
        // check must reject before they're read.

        byte[] dst = new byte[100];
        LzfseDecoder.Decompress(dst, stream, out DecompressStatus status);

        status.Should().NotBe(DecompressStatus.Ok,
            "a truncated V2 fixed header must be rejected, never accepted");
    }

    [Fact]
    public void Decompress_V2BlockWithInflatedHeaderSize_ReportsMalformed()
    {
        // DecodeV2ToV1 reads the "header_size" field from packed_fields[2] bits 0..31.
        // A crafted stream can set this arbitrarily; the decoder must reject it rather
        // than run off the end of its input buffer. Before the guard, this caused an
        // IndexOutOfRangeException in the freq-table refill loop.
        byte[] stream = new byte[32 + 4];  // fixed V2 header + EOS magic, no freq data
        MemoryOperations.Store4(stream, Constants.CompressedV2BlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 0); // n_raw_bytes = 0

        // packed_fields[2] at offset 24; bits [0:31] = header_size = 1,000,000 (far beyond buffer).
        ulong packedField2 = 1_000_000UL;
        MemoryOperations.Store8(stream.AsSpan(24), packedField2);

        MemoryOperations.Store4(stream.AsSpan(32), Constants.EndOfStreamBlockMagic);

        byte[] dst = new byte[100];
        LzfseDecoder.Decompress(dst, stream, out DecompressStatus status);

        status.Should().Be(DecompressStatus.Malformed,
            "a V2 header claiming more bytes than the stream provides must be reported as malformed, not crash");
    }

    [Fact]
    public void Decompress_V2BlockWithUndersizedHeaderSize_ReportsMalformed()
    {
        // Inverse case: declaredHeaderSize < 32 (the fixed header size). Should reject
        // up-front rather than enter the freq loop with sourcePosition (32) > sourceEnd.
        byte[] stream = new byte[32 + 4];
        MemoryOperations.Store4(stream, Constants.CompressedV2BlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 0);

        ulong packedField2 = 16UL; // header_size = 16, less than FreqTablesOffset (32)
        MemoryOperations.Store8(stream.AsSpan(24), packedField2);

        MemoryOperations.Store4(stream.AsSpan(32), Constants.EndOfStreamBlockMagic);

        byte[] dst = new byte[100];
        LzfseDecoder.Decompress(dst, stream, out DecompressStatus status);

        status.Should().Be(DecompressStatus.Malformed);
    }

    [Fact]
    public void DecompressAllocating_EndOfStreamOnly_ReturnsEmptyArray()
    {
        ReadOnlySpan<byte> src = [0x62, 0x76, 0x78, 0x24];

        byte[] result = LzfseDecoder.Decompress(src);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DecompressAllocating_UncompressedBlock_ReturnsExactSizeArray()
    {
        byte[] src = [
            0x62, 0x76, 0x78, 0x2d,              // bvx-
            0x05, 0x00, 0x00, 0x00,              // n_raw_bytes: 5
            0x48, 0x65, 0x6c, 0x6c, 0x6f,        // "Hello"
            0x62, 0x76, 0x78, 0x24               // EOS
        ];

        byte[] result = LzfseDecoder.Decompress(src);

        result.Should().HaveCount(5);
        Encoding.ASCII.GetString(result).Should().Be("Hello");
    }

    [Fact]
    public void DecompressAllocating_MultipleUncompressedBlocks_ConcatenatesRawBytes()
    {
        byte[] src = [
            0x62, 0x76, 0x78, 0x2d, 0x03, 0x00, 0x00, 0x00, (byte)'A', (byte)'B', (byte)'C',
            0x62, 0x76, 0x78, 0x2d, 0x02, 0x00, 0x00, 0x00, (byte)'D', (byte)'E',
            0x62, 0x76, 0x78, 0x24
        ];

        byte[] result = LzfseDecoder.Decompress(src);

        Encoding.ASCII.GetString(result).Should().Be("ABCDE");
    }

    [Fact]
    public void DecompressAllocating_EmptyInput_Throws()
    {
        ReadOnlySpan<byte> src = [];

        try
        {
            _ = LzfseDecoder.Decompress(src);
            Assert.Fail("Expected InvalidDataException");
        }
        catch (System.IO.InvalidDataException)
        {
        }
    }

    [Fact]
    public void DecompressAllocating_InvalidMagic_Throws()
    {
        byte[] src = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00];

        Action act = () => LzfseDecoder.Decompress(src);

        act.Should().Throw<System.IO.InvalidDataException>();
    }

    [Fact]
    public void DecompressAllocating_TruncatedUncompressedPayload_Throws()
    {
        byte[] src = [
            0x62, 0x76, 0x78, 0x2d,
            0x05, 0x00, 0x00, 0x00,
            0x48, 0x65                           // only 2 of the 5 declared bytes
        ];

        Action act = () => LzfseDecoder.Decompress(src);

        act.Should().Throw<System.IO.InvalidDataException>();
    }

    [Fact]
    public void DecompressAllocating_MissingEndOfStream_Throws()
    {
        byte[] src = [
            0x62, 0x76, 0x78, 0x2d,
            0x05, 0x00, 0x00, 0x00,
            0x48, 0x65, 0x6c, 0x6c, 0x6f
            // no EOS block
        ];

        Action act = () => LzfseDecoder.Decompress(src);

        act.Should().Throw<System.IO.InvalidDataException>();
    }

    [Fact]
    public void DecompressAllocating_V2HeaderWithInflatedSize_Throws()
    {
        byte[] src = new byte[32 + 4];
        MemoryOperations.Store4(src, Constants.CompressedV2BlockMagic);
        ulong packedField2 = 1_000_000UL;   // header_size far past stream end
        MemoryOperations.Store8(src.AsSpan(24), packedField2);
        MemoryOperations.Store4(src.AsSpan(32), Constants.EndOfStreamBlockMagic);

        Action act = () => LzfseDecoder.Decompress(src);

        act.Should().Throw<System.IO.InvalidDataException>();
    }

    [Fact]
    public void DecompressAllocating_RoundTripMatchesBufferOverload()
    {
        byte[] stream = [
            0x62, 0x76, 0x78, 0x2d, 0x05, 0x00, 0x00, 0x00,
            0x48, 0x65, 0x6c, 0x6c, 0x6f,
            0x62, 0x76, 0x78, 0x24
        ];

        byte[] viaAllocating = LzfseDecoder.Decompress(stream);

        byte[] viaBuffer = new byte[viaAllocating.Length];
        int written = LzfseDecoder.Decompress(viaBuffer, stream, out DecompressStatus status);

        status.Should().Be(DecompressStatus.Ok);
        written.Should().Be(viaAllocating.Length);
        viaBuffer.Should().Equal(viaAllocating);
    }

    [Fact]
    public void Decompress_LzvnBlockWithTruncatedEosMarker_ReportsNonOk()
    {
        // Build a bvxn block whose payload is just {0x06}. PayloadByteCount = 1,
        // so the outer bounds check is satisfied, but the 8-byte EOS marker is truncated.
        byte[] stream = new byte[12 + 1 + 4];
        MemoryOperations.Store4(stream, Constants.CompressedLzvnBlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 8); // claim 8 raw bytes
        MemoryOperations.Store4(stream.AsSpan(8), 1); // payload = 1 byte
        stream[12] = 0x06; // truncated EOS
        MemoryOperations.Store4(stream.AsSpan(13), Constants.EndOfStreamBlockMagic);

        byte[] dst = new byte[100];
        LzfseDecoder.Decompress(dst, stream, out DecompressStatus status);

        status.Should().NotBe(DecompressStatus.Ok, "a truncated EOS marker is not a successful decode");
    }
}
