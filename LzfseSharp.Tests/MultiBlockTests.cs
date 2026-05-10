using AwesomeAssertions;

namespace LzfseSharp.Tests;

/// <summary>
/// Tests that exercise the outer block-framing state machine in
/// <see cref="LzfseDecoder"/>.DecodeInternal — transitions between block types and
/// edge cases that the reference-encoder round-trips don't reliably hit.
///
/// Block magic numbers from src/lzfse_internal.h:
///   bvx$ (0x24787662) end of stream
///   bvx- (0x2d787662) uncompressed
///   bvx1 (0x31787662) LZFSE V1
///   bvx2 (0x32787662) LZFSE V2
///   bvxn (0x6e787662) LZVN
/// </summary>
public class MultiBlockTests
{
    [Fact]
    public void Decompress_UncompressedThenLzvn_ConcatenatesBlocks()
    {
        // Block 1 (bvx-): "Hello" (5 bytes).
        // Block 2 (bvxn): small literal "World" (5 bytes) via sml_l opcode 0xe5.
        // Both contribute to a single decompressed output.
        List<byte> stream = new();

        // Uncompressed block header + 5 bytes.
        AppendUint32(stream, Constants.UncompressedBlockMagic);
        AppendUint32(stream, 5);
        stream.AddRange("Hello"u8.ToArray());

        // LZVN block header: raw=5, payload=14 (1 opcode + 5 literal + 8 EOS).
        AppendUint32(stream, Constants.CompressedLzvnBlockMagic);
        AppendUint32(stream, 5);   // RawByteCount
        AppendUint32(stream, 14);  // PayloadByteCount
        stream.Add(0xe5);          // sml_l L=5
        stream.AddRange("World"u8.ToArray());
        stream.Add(0x06);          // EOS
        for (int i = 0; i < 7; i++) stream.Add(0);

        AppendUint32(stream, Constants.EndOfStreamBlockMagic);

        byte[] dst = new byte[10];
        int result = LzfseDecoder.Decompress(dst, stream.ToArray());

        result.Should().Be(10);
        dst.Should().Equal("HelloWorld"u8.ToArray());
    }

    [Fact]
    public void Decompress_EmptyUncompressedBlock_IsAllowed()
    {
        // n_raw_bytes = 0: a well-formed but empty uncompressed block.
        List<byte> stream = new();
        AppendUint32(stream, Constants.UncompressedBlockMagic);
        AppendUint32(stream, 0);
        AppendUint32(stream, Constants.EndOfStreamBlockMagic);

        byte[] dst = new byte[16];
        int result = LzfseDecoder.Decompress(dst, stream.ToArray());

        result.Should().Be(0);
    }

    [Fact]
    public void Decompress_OversizedDestinationBuffer_ReturnsActualSize()
    {
        // Caller allocated more dst than the stream actually produces. The public API
        // (LzfseDecoder.Decompress) must report the logical size, not the buffer size.
        List<byte> stream = new();
        AppendUint32(stream, Constants.UncompressedBlockMagic);
        AppendUint32(stream, 3);
        stream.AddRange("abc"u8.ToArray());
        AppendUint32(stream, Constants.EndOfStreamBlockMagic);

        byte[] dst = new byte[1024];
        int result = LzfseDecoder.Decompress(dst, stream.ToArray());

        result.Should().Be(3);
        dst[..3].ToArray().Should().Equal("abc"u8.ToArray());
    }

    [Fact]
    public void Decompress_SingleByteOutput_Works()
    {
        // Edge case: smallest possible non-empty decompressed output.
        List<byte> stream = new();
        AppendUint32(stream, Constants.UncompressedBlockMagic);
        AppendUint32(stream, 1);
        stream.Add((byte)'Z');
        AppendUint32(stream, Constants.EndOfStreamBlockMagic);

        byte[] dst = new byte[4];
        int result = LzfseDecoder.Decompress(dst, stream.ToArray());

        result.Should().Be(1);
        dst[0].Should().Be((byte)'Z');
    }

    [Fact]
    public void Decompress_ThreeBlockStream_DecodesInOrder()
    {
        // Three blocks of distinct types in sequence:
        //   1. uncompressed "AAAA"
        //   2. LZVN (sml_l) "BBBB"
        //   3. uncompressed "CCCC"
        List<byte> stream = [];

        AppendUint32(stream, Constants.UncompressedBlockMagic);
        AppendUint32(stream, 4);
        stream.AddRange("AAAA"u8.ToArray());

        AppendUint32(stream, Constants.CompressedLzvnBlockMagic);
        AppendUint32(stream, 4);    // raw
        AppendUint32(stream, 13);   // payload: 1 opcode + 4 literal + 8 EOS
        stream.Add(0xe4);
        stream.AddRange("BBBB"u8.ToArray());
        stream.Add(0x06);
        for (int i = 0; i < 7; i++) stream.Add(0);

        AppendUint32(stream, Constants.UncompressedBlockMagic);
        AppendUint32(stream, 4);
        stream.AddRange("CCCC"u8.ToArray());

        AppendUint32(stream, Constants.EndOfStreamBlockMagic);

        byte[] dst = new byte[12];
        int result = LzfseDecoder.Decompress(dst, stream.ToArray());

        result.Should().Be(12);
        dst.Should().Equal("AAAABBBBCCCC"u8.ToArray());
    }

    [Fact]
    public void Decompress_StreamMissingEosMagic_ReportsError()
    {
        // Valid uncompressed block with no trailing bvx$ marker.
        List<byte> stream = new();
        AppendUint32(stream, Constants.UncompressedBlockMagic);
        AppendUint32(stream, 3);
        stream.AddRange("xyz"u8.ToArray());
        // No EOS — source is truncated from the decoder's perspective.

        byte[] dst = new byte[16];
        int result = LzfseDecoder.Decompress(dst, stream.ToArray());

        result.Should().Be(0, "a stream without bvx$ terminator should be reported as truncated");
    }

    private static void AppendUint32(List<byte> buffer, uint value)
    {
        buffer.Add((byte)(value & 0xff));
        buffer.Add((byte)((value >> 8) & 0xff));
        buffer.Add((byte)((value >> 16) & 0xff));
        buffer.Add((byte)((value >> 24) & 0xff));
    }
}
