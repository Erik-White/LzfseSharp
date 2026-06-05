using System.Text;
using AwesomeAssertions;
using LzfseSharp.Core;
using Xunit;

namespace LzfseSharp.Tests;

public class LzfseDecoderTests
{
    [Fact]
    public void Decompress_EmptyInput_Throws()
    {
        // No bytes at all: the source ends before even a block magic, let alone the
        // end-of-stream marker.
        Action act = () => StreamCodec.Decode([]);

        act.Should().Throw<EndOfStreamException>();
    }

    [Fact]
    public void Decompress_EndOfStreamBlock_ProducesEmptyOutput()
    {
        // End of stream magic: 0x62767824 (little-endian: 24 78 76 62 = "bvx$")
        byte[] result = StreamCodec.Decode([0x62, 0x76, 0x78, 0x24]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Decompress_UncompressedBlock_CopiesData()
    {
        // Magic: bvx-, n_raw_bytes: 5, data "Hello", then end of stream.
        byte[] src = [
            0x62, 0x76, 0x78, 0x2d,  // Magic: bvx-
            0x05, 0x00, 0x00, 0x00,  // n_raw_bytes: 5
            0x48, 0x65, 0x6c, 0x6c, 0x6f,  // "Hello"
            0x62, 0x76, 0x78, 0x24   // End of stream magic
        ];

        byte[] result = StreamCodec.Decode(src);

        Encoding.ASCII.GetString(result).Should().Be("Hello");
    }

    [Fact]
    public void Decompress_InvalidMagic_Throws()
    {
        Action act = () => StreamCodec.Decode([0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00]);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Decompress_TruncatedHeader_Throws()
    {
        // Uncompressed block magic but missing the n_raw_bytes field.
        Action act = () => StreamCodec.Decode([0x62, 0x76, 0x78, 0x2d]);

        act.Should().Throw<EndOfStreamException>();
    }

    [Fact]
    public void Decompress_LzvnSmallLiteralFollowedByUncompressedBlock_DoesNotCorruptOutput()
    {
        // Regression guard for the CopyLiteralBytes fast path: when L <= 3 and
        // destinationLength >= 4, it stores 4 bytes and advances by L, leaving
        // 4-L "overshoot" bytes past the logical write position. The outer framer's
        // DestinationEnd clamp must prevent those bytes from leaking into the logical
        // output of a subsequent block.

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

        byte[] result = StreamCodec.Decode(stream);

        result.Should().Equal((byte)'A', (byte)'B', (byte)'Y', (byte)'Z');
    }

    [Theory]
    [InlineData(28)] // bvx2 magic claims a header but fewer than 32 bytes of fixed-header data follow
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(31)]
    public void Decompress_V2BlockWithIncompleteFixedHeader_Throws(int totalLength)
    {
        byte[] stream = new byte[totalLength];
        MemoryOperations.Store4(stream, Constants.CompressedV2BlockMagic);
        // Remaining bytes are zeros — their contents don't matter; the outer bounds
        // check must reject before they're read.

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<EndOfStreamException>(
            "a truncated V2 fixed header must be rejected, never accepted");
    }

    [Fact]
    public void Decompress_V2BlockWithInflatedHeaderSize_ThrowsInvalidData()
    {
        // DecodeV2ToV1 reads the "header_size" field from packed_fields[2] bits 0..31.
        // A crafted stream can set this arbitrarily; the decoder must reject it rather
        // than run off the end of its input buffer.
        byte[] stream = new byte[32 + 4];  // fixed V2 header + EOS magic, no freq data
        MemoryOperations.Store4(stream, Constants.CompressedV2BlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 0); // n_raw_bytes = 0

        // packed_fields[2] at offset 24; bits [0:31] = header_size = 1,000,000 (far beyond buffer).
        ulong packedField2 = 1_000_000UL;
        MemoryOperations.Store8(stream.AsSpan(24), packedField2);

        MemoryOperations.Store4(stream.AsSpan(32), Constants.EndOfStreamBlockMagic);

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<InvalidDataException>(
            "a V2 header claiming more bytes than the stream provides must be reported as malformed, not crash");
    }

    [Fact]
    public void Decompress_V2BlockWithUndersizedHeaderSize_ThrowsInvalidData()
    {
        // Inverse case: declaredHeaderSize < 32 (the fixed header size). Should reject
        // up-front rather than enter the freq loop with sourcePosition (32) > sourceEnd.
        byte[] stream = new byte[32 + 4];
        MemoryOperations.Store4(stream, Constants.CompressedV2BlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 0);

        ulong packedField2 = 16UL; // header_size = 16, less than FreqTablesOffset (32)
        MemoryOperations.Store8(stream.AsSpan(24), packedField2);

        MemoryOperations.Store4(stream.AsSpan(32), Constants.EndOfStreamBlockMagic);

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Decompress_MultipleUncompressedBlocks_ConcatenatesRawBytes()
    {
        byte[] src = [
            0x62, 0x76, 0x78, 0x2d, 0x03, 0x00, 0x00, 0x00, (byte)'A', (byte)'B', (byte)'C',
            0x62, 0x76, 0x78, 0x2d, 0x02, 0x00, 0x00, 0x00, (byte)'D', (byte)'E',
            0x62, 0x76, 0x78, 0x24
        ];

        byte[] result = StreamCodec.Decode(src);

        Encoding.ASCII.GetString(result).Should().Be("ABCDE");
    }

    [Fact]
    public void Decompress_TruncatedUncompressedPayload_Throws()
    {
        byte[] src = [
            0x62, 0x76, 0x78, 0x2d,
            0x05, 0x00, 0x00, 0x00,
            0x48, 0x65                           // only 2 of the 5 declared bytes
        ];

        Action act = () => StreamCodec.Decode(src);

        act.Should().Throw<EndOfStreamException>();
    }

    [Fact]
    public void Decompress_MissingEndOfStream_Throws()
    {
        byte[] src = [
            0x62, 0x76, 0x78, 0x2d,
            0x05, 0x00, 0x00, 0x00,
            0x48, 0x65, 0x6c, 0x6c, 0x6f
            // no EOS block
        ];

        Action act = () => StreamCodec.Decode(src);

        act.Should().Throw<EndOfStreamException>();
    }

    [Fact]
    public void Decompress_DeclaredSizeExceedsInt32_DecodesUntilTruncation()
    {
        // LZVN block header claims 2^31 raw bytes but only a short payload follows.
        // The streaming decoder no longer pre-scans the total size, so it simply
        // decodes what it can and then reports the truncated stream.
        byte[] stream = new byte[12 + 4 + 4];
        MemoryOperations.Store4(stream, Constants.CompressedLzvnBlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 0x80000000u);    // n_raw_bytes = 2^31
        MemoryOperations.Store4(stream.AsSpan(8), 4);              // payload_bytes = 4
        MemoryOperations.Store4(stream.AsSpan(16), Constants.EndOfStreamBlockMagic);

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<Exception>().Which.Should().Match(
            e => e is InvalidDataException || e is EndOfStreamException);
    }

    [Fact]
    public void Decompress_LzvnBlockWithOversizedDeclaredPayload_ThrowsInvalidData()
    {
        // A bvxn header declaring a payload larger than MaxBlockPayloadBytes must be
        // rejected as malformed, not drive the source buffer to grow without bound (which
        // would exhaust memory and eventually throw an undocumented overflow exception)
        // while it waits for bytes that will never arrive.
        byte[] stream = new byte[12 + 4];
        MemoryOperations.Store4(stream, Constants.CompressedLzvnBlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 16);   // n_raw_bytes
        MemoryOperations.Store4(stream.AsSpan(8), (uint)Constants.MaxBlockPayloadBytes + 1);
        MemoryOperations.Store4(stream.AsSpan(12), Constants.EndOfStreamBlockMagic);

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Decompress_V2HeaderWithInflatedSize_Throws()
    {
        byte[] src = new byte[32 + 4];
        MemoryOperations.Store4(src, Constants.CompressedV2BlockMagic);
        ulong packedField2 = 1_000_000UL;   // header_size far past stream end
        MemoryOperations.Store8(src.AsSpan(24), packedField2);
        MemoryOperations.Store4(src.AsSpan(32), Constants.EndOfStreamBlockMagic);

        Action act = () => StreamCodec.Decode(src);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Decompress_V2BlockWithCorruptFreqBits_ThrowsInvalidData()
    {
        // A V2 block whose fixed 32-byte header passes outer bounds checks
        // (declared header_size = 33 is in range) but whose 1-byte freq-table bit
        // stream can't supply enough bits for all 360 freq symbols.
        const int declaredHeaderSize = 33;
        byte[] stream = new byte[declaredHeaderSize + 4];

        MemoryOperations.Store4(stream, Constants.CompressedV2BlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 0);                 // n_raw_bytes
        MemoryOperations.Store8(stream.AsSpan(24), (ulong)declaredHeaderSize); // packed_fields[2]: header_size in bits [0:31]
        stream[32] = 0x00;                                            // 1 byte of freq data — not enough for 360 symbols

        MemoryOperations.Store4(stream.AsSpan(33), Constants.EndOfStreamBlockMagic);

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task DecompressAsync_MatchesSyncOutput()
    {
        byte[] stream = [
            0x62, 0x76, 0x78, 0x2d, 0x05, 0x00, 0x00, 0x00,
            0x48, 0x65, 0x6c, 0x6c, 0x6f,
            0x62, 0x76, 0x78, 0x24
        ];

        byte[] viaSync = StreamCodec.Decode(stream);
        byte[] viaAsync = await StreamCodec.DecodeAsync(stream);

        viaAsync.Should().Equal(viaSync);
        Encoding.ASCII.GetString(viaAsync).Should().Be("Hello");
    }

    [Fact]
    public void Decompress_LzvnBlockWithTruncatedEosMarker_Throws()
    {
        // A bvxn block whose payload is just {0x06}. PayloadByteCount = 1, so the outer
        // bounds check is satisfied, but the 8-byte EOS marker is truncated.
        byte[] stream = new byte[12 + 1 + 4];
        MemoryOperations.Store4(stream, Constants.CompressedLzvnBlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 8); // claim 8 raw bytes
        MemoryOperations.Store4(stream.AsSpan(8), 1); // payload = 1 byte
        stream[12] = 0x06; // truncated EOS
        MemoryOperations.Store4(stream.AsSpan(13), Constants.EndOfStreamBlockMagic);

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<Exception>("a truncated EOS marker is not a successful decode")
            .Which.Should().Match(e => e is InvalidDataException || e is EndOfStreamException);
    }

    [Fact]
    public void Decompress_NullSource_Throws()
    {
        using MemoryStream destination = new();
        Action act = () => LzfseDecoder.Decompress(null!, destination);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decompress_NullDestination_Throws()
    {
        using MemoryStream source = new([0x62, 0x76, 0x78, 0x24]);
        Action act = () => LzfseDecoder.Decompress(source, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
