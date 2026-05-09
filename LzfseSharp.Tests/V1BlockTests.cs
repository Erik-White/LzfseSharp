using AwesomeAssertions;
using Lzfse;
using LzfseSharp.Core;
using LzfseSharp.Decoder;

namespace LzfseSharp.Tests;

/// <summary>
/// Tests for LZFSE V1 ("bvx1") blocks. The reference compressor only ever emits V2 or LZVN
/// blocks, so these tests hand-craft V1 blocks by repacking reference-produced V2 blocks.
/// </summary>
public class V1BlockTests
{
    [Fact]
    public void Decompress_V1BlockRepackedFromV2_MatchesOriginal()
    {
        byte[] original = new byte[50000];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i % 37);

        byte[] compressedBuffer = new byte[original.Length * 2 + 1024];
        int compressedSize = LzfseCompressor.Compress(original, compressedBuffer);
        byte[] v2Stream = compressedBuffer.AsSpan(0, compressedSize).ToArray();

        byte[] v1Stream = RepackV2BlocksAsV1(v2Stream, out int v2BlocksFound);
        v2BlocksFound.Should().BeGreaterThan(0, "the reference must emit at least one V2 block for this test to be meaningful");

        byte[] decompressed = new byte[original.Length];
        int bytesWritten = LzfseDecoder.Decompress(decompressed, v1Stream);

        bytesWritten.Should().Be(original.Length);
        decompressed.Should().Equal(original);
    }

    [Fact]
    public void Decompress_V1BlockTruncatedHeader_ReturnsZero()
    {
        // Just the magic, no header body. Must be rejected (not read past the end).
        byte[] src = [0x62, 0x76, 0x78, 0x31];
        byte[] dst = new byte[100];

        int result = LzfseDecoder.Decompress(dst, src);

        result.Should().Be(0);
    }

    private delegate void HeaderMutation(ref LzfseCompressedBlockHeaderV1 header);

    [Fact]
    public void Decompress_V1BlockWithNLiteralsNotMultipleOf4_Rejected()
    {
        byte[] v1Stream = RepackReferenceAsV1((ref LzfseCompressedBlockHeaderV1 h) => h.NLiterals -= 1);

        byte[] dst = new byte[50000];
        int result = LzfseDecoder.Decompress(dst, v1Stream);

        result.Should().Be(0, "NLiterals not divisible by 4 must be rejected by CheckBlockHeaderV1");
    }

    [Fact]
    public void Decompress_V1BlockWithMismatchedPayloadSum_Rejected()
    {
        byte[] v1Stream = RepackReferenceAsV1((ref LzfseCompressedBlockHeaderV1 h) => h.NPayloadBytes += 1);

        byte[] dst = new byte[50000];
        int result = LzfseDecoder.Decompress(dst, v1Stream);

        result.Should().Be(0, "NPayloadBytes disagreeing with the literal/LMD sum must be rejected");
    }

    [Fact]
    public void Decompress_V1BlockWithFreqSumBelowStateCount_Rejected()
    {
        // Shave 1 off the largest entry in the literal frequency table so the sum
        // drops below EncodeLiteralStates. Under the old CheckFreq (sum > n), this
        // would pass validation and InitDecoderTable would leave a tail of stale
        // entries. Under the tightened check (sum != n) it must be rejected.
        byte[] v1Stream = RepackReferenceAsV1((ref LzfseCompressedBlockHeaderV1 header) =>
        {
            int maxIdx = 0;
            for (int i = 1; i < header.LiteralFreq.Length; i++)
            {
                if (header.LiteralFreq[i] > header.LiteralFreq[maxIdx])
                    maxIdx = i;
            }
            header.LiteralFreq[maxIdx] -= 1;
        });

        byte[] dst = new byte[50000];
        int result = LzfseDecoder.Decompress(dst, v1Stream);

        result.Should().Be(0, "a literal frequency table whose sum is below EncodeLiteralStates must be rejected");
    }

    private static byte[] RepackReferenceAsV1(HeaderMutation mutate)
    {
        // Input must be large enough that the reference encoder picks a bvx2 block
        // (it prefers LZVN for small inputs).
        byte[] original = new byte[50000];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i % 37);

        byte[] compressedBuffer = new byte[original.Length * 2 + 1024];
        int compressedSize = LzfseCompressor.Compress(original, compressedBuffer);
        ReadOnlySpan<byte> v2Stream = compressedBuffer.AsSpan(0, compressedSize);

        // Walk until the first bvx2 block, decode its header, mutate, repack, and
        // splice back into the stream.
        using MemoryStream output = new();
        int pos = 0;
        bool mutated = false;
        while (pos < v2Stream.Length)
        {
            uint magic = MemoryOperations.Load4(v2Stream[pos..]);
            switch (magic)
            {
                case Constants.EndOfStreamBlockMagic:
                    output.Write(v2Stream.Slice(pos, 4));
                    pos = v2Stream.Length;
                    break;

                case Constants.UncompressedBlockMagic:
                {
                    int rawBytes = (int)MemoryOperations.Load4(v2Stream[(pos + 4)..]);
                    int blockSize = 8 + rawBytes;
                    output.Write(v2Stream.Slice(pos, blockSize));
                    pos += blockSize;
                    break;
                }

                case Constants.CompressedLzvnBlockMagic:
                {
                    int payloadBytes = (int)MemoryOperations.Load4(v2Stream[(pos + 8)..]);
                    int blockSize = 12 + payloadBytes;
                    output.Write(v2Stream.Slice(pos, blockSize));
                    pos += blockSize;
                    break;
                }

                case Constants.CompressedV2BlockMagic:
                {
                    BlockHeaderDecoder.V2ToV1DecodeResult decoded = BlockHeaderDecoder.DecodeV2ToV1(v2Stream[pos..]);
                    decoded.Status.Should().Be(0);
                    LzfseCompressedBlockHeaderV1 h = decoded.Header;
                    if (!mutated)
                    {
                        mutate(ref h);
                        mutated = true;
                    }
                    output.Write(EncodeV1Header(h));
                    int payloadLen = (int)(decoded.Header.NLiteralPayloadBytes + decoded.Header.NLmdPayloadBytes);
                    output.Write(v2Stream.Slice(pos + decoded.HeaderSize, payloadLen));
                    pos += decoded.HeaderSize + payloadLen;
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unexpected magic 0x{magic:X8} at {pos}");
            }
        }

        mutated.Should().BeTrue("test relies on the reference encoder emitting at least one bvx2 block");
        return output.ToArray();
    }

    private static byte[] RepackV2BlocksAsV1(ReadOnlySpan<byte> v2Stream, out int v2BlocksConverted)
    {
        v2BlocksConverted = 0;
        using MemoryStream output = new();
        int pos = 0;

        while (pos < v2Stream.Length)
        {
            uint magic = MemoryOperations.Load4(v2Stream[pos..]);
            switch (magic)
            {
                case Constants.EndOfStreamBlockMagic:
                    output.Write(v2Stream.Slice(pos, 4));
                    return output.ToArray();

                case Constants.UncompressedBlockMagic:
                {
                    int rawBytes = (int)MemoryOperations.Load4(v2Stream[(pos + 4)..]);
                    int blockSize = 8 + rawBytes;
                    output.Write(v2Stream.Slice(pos, blockSize));
                    pos += blockSize;
                    break;
                }

                case Constants.CompressedLzvnBlockMagic:
                {
                    int payloadBytes = (int)MemoryOperations.Load4(v2Stream[(pos + 8)..]);
                    int blockSize = 12 + payloadBytes;
                    output.Write(v2Stream.Slice(pos, blockSize));
                    pos += blockSize;
                    break;
                }

                case Constants.CompressedV2BlockMagic:
                {
                    BlockHeaderDecoder.V2ToV1DecodeResult decoded = BlockHeaderDecoder.DecodeV2ToV1(v2Stream[pos..]);
                    decoded.Status.Should().Be(0);

                    output.Write(EncodeV1Header(decoded.Header));

                    int payloadLen = (int)(decoded.Header.NLiteralPayloadBytes + decoded.Header.NLmdPayloadBytes);
                    output.Write(v2Stream.Slice(pos + decoded.HeaderSize, payloadLen));

                    pos += decoded.HeaderSize + payloadLen;
                    v2BlocksConverted++;
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unexpected block magic 0x{magic:X8} at offset {pos}");
            }
        }

        return output.ToArray();
    }

    private static byte[] EncodeV1Header(LzfseCompressedBlockHeaderV1 header)
    {
        const int V1HeaderSize = 8 * sizeof(uint) + 4 * sizeof(ushort)
                               + sizeof(uint) + 3 * sizeof(ushort)
                               + sizeof(ushort) * (Constants.EncodeLSymbols + Constants.EncodeMSymbols +
                                                   Constants.EncodeDSymbols + Constants.EncodeLiteralSymbols);

        byte[] buffer = new byte[V1HeaderSize];
        Span<byte> s = buffer;

        MemoryOperations.Store4(s, Constants.CompressedV1BlockMagic);
        MemoryOperations.Store4(s[4..], header.NRawBytes);
        MemoryOperations.Store4(s[8..], header.NPayloadBytes);
        MemoryOperations.Store4(s[12..], header.NLiterals);
        MemoryOperations.Store4(s[16..], header.NMatches);
        MemoryOperations.Store4(s[20..], header.NLiteralPayloadBytes);
        MemoryOperations.Store4(s[24..], header.NLmdPayloadBytes);
        MemoryOperations.Store4(s[28..], unchecked((uint)header.LiteralBits));

        MemoryOperations.Store2(s[32..], header.LiteralState[0]);
        MemoryOperations.Store2(s[34..], header.LiteralState[1]);
        MemoryOperations.Store2(s[36..], header.LiteralState[2]);
        MemoryOperations.Store2(s[38..], header.LiteralState[3]);

        MemoryOperations.Store4(s[40..], unchecked((uint)header.LmdBits));
        MemoryOperations.Store2(s[44..], header.LState);
        MemoryOperations.Store2(s[46..], header.MState);
        MemoryOperations.Store2(s[48..], header.DState);

        int offset = 50;
        for (int i = 0; i < Constants.EncodeLSymbols; i++, offset += 2)
            MemoryOperations.Store2(s[offset..], header.LFreq[i]);
        for (int i = 0; i < Constants.EncodeMSymbols; i++, offset += 2)
            MemoryOperations.Store2(s[offset..], header.MFreq[i]);
        for (int i = 0; i < Constants.EncodeDSymbols; i++, offset += 2)
            MemoryOperations.Store2(s[offset..], header.DFreq[i]);
        for (int i = 0; i < Constants.EncodeLiteralSymbols; i++, offset += 2)
            MemoryOperations.Store2(s[offset..], header.LiteralFreq[i]);

        return buffer;
    }
}
