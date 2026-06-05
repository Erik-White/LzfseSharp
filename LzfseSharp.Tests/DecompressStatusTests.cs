using AwesomeAssertions;
using LzfseSharp.Core;
using Lzfse;
using Xunit;

namespace LzfseSharp.Tests;

/// <summary>
/// Verifies that the stream-based decoder maps each failure mode to the documented
/// exception type, and succeeds on a valid reference round-trip. Replaces the former
/// status-enum tests now that the API reports outcomes via exceptions.
/// </summary>
public class DecompressOutcomeTests
{
    [Fact]
    public void Succeeds_OnReferenceRoundTrip()
    {
        byte[] original = "The quick brown fox jumps over the lazy dog."u8.ToArray();
        byte[] compressedBuffer = new byte[original.Length * 2 + 1024];
        int size = LzfseCompressor.Compress(original, compressedBuffer);

        byte[] result = StreamCodec.Decode(compressedBuffer.AsSpan(0, size).ToArray());

        result.Should().Equal(original);
    }

    [Fact]
    public void Throws_EndOfStream_WhenEmptyInput()
    {
        Action act = () => StreamCodec.Decode([]);

        act.Should().Throw<EndOfStreamException>();
    }

    [Fact]
    public void Throws_EndOfStream_WhenBlockMagicIncomplete()
    {
        byte[] stream = [0x62, 0x76, 0x78]; // 3 of 4 magic bytes

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<EndOfStreamException>();
    }

    [Fact]
    public void Throws_EndOfStream_WhenUncompressedPayloadShort()
    {
        // bvx- header claims 10 bytes of raw data but only 3 are present.
        byte[] stream = new byte[8 + 3];
        MemoryOperations.Store4(stream, Constants.UncompressedBlockMagic);
        MemoryOperations.Store4(stream.AsSpan(4), 10);
        stream[8] = (byte)'x';
        stream[9] = (byte)'y';
        stream[10] = (byte)'z';

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<EndOfStreamException>();
    }

    [Fact]
    public void Throws_InvalidData_WhenBlockMagicUnknown()
    {
        byte[] stream = [0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0];

        Action act = () => StreamCodec.Decode(stream);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Succeeds_WhenDecompressedOutputExceedsAnyFixedBuffer()
    {
        // The old API could report DestinationFull when a caller's buffer was too small.
        // The stream-based API has no such failure mode: output of any size streams to the
        // destination. Decode a large highly-compressible input to exercise the window
        // flush path and confirm the full output is delivered.
        byte[] original = new byte[2_000_000];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i % 7);

        byte[] compressedBuffer = new byte[original.Length + 1024];
        int size = LzfseCompressor.Compress(original, compressedBuffer);

        byte[] result = StreamCodec.Decode(compressedBuffer.AsSpan(0, size).ToArray());

        result.Should().Equal(original);
    }
}
