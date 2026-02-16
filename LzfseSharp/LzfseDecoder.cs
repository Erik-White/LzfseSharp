using LzfseSharp.Core;
using LzfseSharp.Decoder;
using LzfseSharp.Fse;
using LzfseSharp.Lzvn;

namespace LzfseSharp;

/// <summary>
/// Main LZFSE decoder providing the public API for decompressing LZFSE-compressed data.
/// </summary>
public static class LzfseDecoder
{
    /// <summary>
    /// Decompresses LZFSE-compressed data from source buffer to destination buffer.
    /// </summary>
    /// <param name="dstBuffer">Destination buffer for decompressed data</param>
    /// <param name="srcBuffer">Source buffer containing compressed data</param>
    /// <returns>The number of bytes written to the destination buffer. Returns 0 on error.</returns>
    /// <remarks>
    /// This method decompresses the entire compressed stream. If the destination buffer is not
    /// large enough to hold the entire decompressed output, only the first dstBuffer.Length bytes
    /// will be written and that length will be returned.
    /// </remarks>
    public static int Decompress(Span<byte> dstBuffer, ReadOnlySpan<byte> srcBuffer)
    {
        if (srcBuffer.Length == 0 || dstBuffer.Length == 0)
            return 0;

        LzfseDecoderState state = default;
        state.SrcBuffer = srcBuffer;
        state.DstBuffer = dstBuffer;
        state.Src = 0;
        state.SrcBegin = 0;
        state.SrcEnd = srcBuffer.Length;
        state.Dst = 0;
        state.DstBegin = 0;
        state.DstEnd = dstBuffer.Length;
        state.EndOfStream = false;
        state.BlockMagic = Constants.NoBlockMagic;
        state.CompressedLzfseBlockState = new LzfseCompressedBlockDecoderState();

        int result = DecodeInternal(ref state);

        // Return bytes written on success or when destination is full, 0 on error
        return (result == Constants.StatusOk || result == Constants.StatusDstFull)
            ? state.Dst - state.DstBegin
            : 0;
    }

    /// <summary>
    /// Internal decoding function that processes all blocks in the stream.
    /// </summary>
    private static int DecodeInternal(ref LzfseDecoderState s)
    {
        while (true)
        {
            // Are we inside a block?
            switch (s.BlockMagic)
            {
                case Constants.NoBlockMagic:
                {
                    // We need at least 4 bytes of magic number to identify next block
                    if (s.Src + 4 > s.SrcEnd)
                        return Constants.StatusSrcEmpty; // Source truncated

                    uint magic = MemoryOperations.Load4(s.SrcBuffer[s.Src..]);

                    // End of stream block
                    if (magic == Constants.EndOfStreamBlockMagic)
                    {
                        s.Src += 4;
                        s.EndOfStream = true;
                        return Constants.StatusOk; // Done
                    }

                    // Uncompressed block
                    if (magic == Constants.UncompressedBlockMagic)
                    {
                        if (s.Src + 8 > s.SrcEnd)
                            return Constants.StatusSrcEmpty; // Source truncated

                        // Setup state for uncompressed block
                        ref UncompressedBlockDecoderState bs = ref s.UncompressedBlockState;
                        bs.NRawBytes = MemoryOperations.Load4(s.SrcBuffer[(s.Src + 4)..]);
                        s.Src += 8; // sizeof(UncompressedBlockHeader)
                        s.BlockMagic = magic;
                        break;
                    }

                    // LZVN compressed block
                    if (magic == Constants.CompressedLzvnBlockMagic)
                    {
                        if (s.Src + 12 > s.SrcEnd)
                            return Constants.StatusSrcEmpty; // Source truncated

                        // Setup state for compressed LZVN block
                        ref LzvnCompressedBlockDecoderState bs = ref s.CompressedLzvnBlockState;
                        bs.NRawBytes = MemoryOperations.Load4(s.SrcBuffer[(s.Src + 4)..]);
                        bs.NPayloadBytes = MemoryOperations.Load4(s.SrcBuffer[(s.Src + 8)..]);
                        bs.DPrev = 0;
                        s.Src += 12; // sizeof(LzvnCompressedBlockHeader)
                        s.BlockMagic = magic;
                        break;
                    }

                    // LZFSE compressed blocks (V1 or V2)
                    if (magic == Constants.CompressedV1BlockMagic || magic == Constants.CompressedV2BlockMagic)
                    {
                        LzfseCompressedBlockHeaderV1 header1;
                        uint headerSize;

                        // Decode compressed headers
                        if (magic == Constants.CompressedV2BlockMagic)
                        {
                            // Check we have the fixed part of the structure (magic + n_raw_bytes + 3x8 packed_fields)
                            if (s.Src + 28 > s.SrcEnd)
                                return Constants.StatusSrcEmpty; // Source truncated

                            // Decode V2 header - this determines actual header size during parsing
                            int decodeStatus = BlockHeaderDecoder.DecodeV2ToV1(out header1, out int actualHeaderSize, s.SrcBuffer[s.Src..]);
                            if (decodeStatus != 0)
                                return Constants.StatusError; // Failed

                            headerSize = (uint)actualHeaderSize;
                        }
                        else
                        {
                            // V1 header - fixed size
                            const uint v1HeaderSize = 8 + 8 + 8 + 8 + 4 * 2 + 8 +
                                                     2 * (Constants.EncodeLSymbols + Constants.EncodeMSymbols +
                                                         Constants.EncodeDSymbols + Constants.EncodeLiteralSymbols);

                            if (s.Src + v1HeaderSize > s.SrcEnd)
                                return Constants.StatusSrcEmpty; // Source truncated

                            header1 = DecodeV1Header(s.SrcBuffer[s.Src..]);
                            headerSize = v1HeaderSize;
                        }

                        // We require the header + entire encoded block to be present in source
                        // during the entire block decoding
                        if (s.Src + headerSize + header1.NLiteralPayloadBytes + header1.NLmdPayloadBytes > s.SrcEnd)
                            return Constants.StatusSrcEmpty; // Need all encoded block

                        // Sanity checks
                        if (BlockHeaderDecoder.CheckBlockHeaderV1(ref header1) != 0)
                            return Constants.StatusError;

                        // Skip header
                        s.Src += (int)headerSize;

                        // Setup state for compressed V1 block from header
                        ref LzfseCompressedBlockDecoderState bs = ref s.CompressedLzfseBlockState;
                        bs.NLmdPayloadBytes = header1.NLmdPayloadBytes;
                        bs.NMatches = header1.NMatches;

                        // Initialize FSE decoder tables
                        FseDecoder.InitDecoderTable(
                            Constants.EncodeLiteralStates,
                            Constants.EncodeLiteralSymbols,
                            header1.LiteralFreq,
                            bs.LiteralDecoder);

                        FseDecoder.InitValueDecoderTable(
                            Constants.EncodeLStates,
                            Constants.EncodeLSymbols,
                            header1.LFreq,
                            Constants.LExtraBits,
                            Constants.LBaseValue,
                            bs.LDecoder);

                        FseDecoder.InitValueDecoderTable(
                            Constants.EncodeMStates,
                            Constants.EncodeMSymbols,
                            header1.MFreq,
                            Constants.MExtraBits,
                            Constants.MBaseValue,
                            bs.MDecoder);

                        FseDecoder.InitValueDecoderTable(
                            Constants.EncodeDStates,
                            Constants.EncodeDSymbols,
                            header1.DFreq,
                            Constants.DExtraBits,
                            Constants.DBaseValue,
                            bs.DDecoder);

                        // Decode literals
                        {
                            FseInStream inStream = default;
                            int bufStart = s.SrcBegin;
                            int literalPayloadEnd = s.Src + (int)header1.NLiteralPayloadBytes;
                            int buf = literalPayloadEnd; // Read bits backwards from the end

                            if (inStream.Init(header1.LiteralBits, ref buf, bufStart, s.SrcBuffer) != 0)
                                return Constants.StatusError;

                            ushort state0 = header1.LiteralState[0];
                            ushort state1 = header1.LiteralState[1];
                            ushort state2 = header1.LiteralState[2];
                            ushort state3 = header1.LiteralState[3];

                            // Decode literals 4 at a time (n_literals is multiple of 4)
                            for (uint i = 0; i < header1.NLiterals; i += 4)
                            {
                                if (inStream.Flush(ref buf, bufStart, s.SrcBuffer) != 0)
                                    return Constants.StatusError;

                                bs.Literals[i + 0] = FseDecoder.Decode(ref state0, bs.LiteralDecoder, ref inStream);
                                bs.Literals[i + 1] = FseDecoder.Decode(ref state1, bs.LiteralDecoder, ref inStream);
                                bs.Literals[i + 2] = FseDecoder.Decode(ref state2, bs.LiteralDecoder, ref inStream);
                                bs.Literals[i + 3] = FseDecoder.Decode(ref state3, bs.LiteralDecoder, ref inStream);
                            }

                            bs.CurrentLiteralPos = 0;
                        }

                        // Skip literal payload
                        s.Src += (int)header1.NLiteralPayloadBytes;

                        // Initialize the L,M,D decode stream, do not start decoding matches yet
                        {
                            FseInStream inStream = default;
                            // Read bits backwards from the end
                            int buf = s.Src + (int)header1.NLmdPayloadBytes;

                            if (inStream.Init(header1.LmdBits, ref buf, s.Src, s.SrcBuffer) != 0)
                                return Constants.StatusError;

                            bs.LState = header1.LState;
                            bs.MState = header1.MState;
                            bs.DState = header1.DState;
                            bs.LmdInBuf = buf - s.Src;
                            bs.LValue = bs.MValue = 0;
                            // Initialize D to an illegal value so we can't erroneously use
                            // an uninitialized "previous" value
                            bs.DValue = -1;
                            bs.LmdInStream = inStream;
                        }

                        s.BlockMagic = magic;
                        break;
                    }

                    // Here we have an invalid magic number
                    return Constants.StatusError;
                }

                case Constants.UncompressedBlockMagic:
                {
                    ref UncompressedBlockDecoderState bs = ref s.UncompressedBlockState;

                    // Compute the size (in bytes) of the data that we will actually copy.
                    // This size is minimum(bs.n_raw_bytes, space in src, space in dst).
                    uint copySize = bs.NRawBytes; // Bytes left to copy
                    if (copySize == 0)
                    {
                        s.BlockMagic = Constants.NoBlockMagic;
                        break; // End of block
                    }

                    if (s.SrcEnd <= s.Src)
                        return Constants.StatusSrcEmpty; // Need more source data

                    int srcSpace = s.SrcEnd - s.Src;
                    if (copySize > srcSpace)
                        copySize = (uint)srcSpace; // Limit to source data (> 0)

                    if (s.DstEnd <= s.Dst)
                        return Constants.StatusDstFull; // Need more destination capacity

                    int dstSpace = s.DstEnd - s.Dst;
                    if (copySize > dstSpace)
                        copySize = (uint)dstSpace; // Limit to destination capacity (> 0)

                    // Copy the data (we always have copySize > 0 here)
                    s.SrcBuffer.Slice(s.Src, (int)copySize).CopyTo(s.DstBuffer[s.Dst..]);
                    s.Src += (int)copySize;
                    s.Dst += (int)copySize;
                    bs.NRawBytes -= copySize;

                    break;
                }

                case Constants.CompressedV1BlockMagic:
                case Constants.CompressedV2BlockMagic:
                {
                    ref LzfseCompressedBlockDecoderState bs = ref s.CompressedLzfseBlockState;

                    // Require the entire LMD payload to be in source
                    if (s.SrcEnd <= s.Src || bs.NLmdPayloadBytes > (uint)(s.SrcEnd - s.Src))
                        return Constants.StatusSrcEmpty;

                    int status = LzfseBlockDecoder.DecodeLmd(ref s);
                    if (status != Constants.StatusOk)
                        return status;

                    s.BlockMagic = Constants.NoBlockMagic;
                    s.Src += (int)bs.NLmdPayloadBytes; // To next block
                    break;
                }

                case Constants.CompressedLzvnBlockMagic:
                {
                    ref LzvnCompressedBlockDecoderState bs = ref s.CompressedLzvnBlockState;

                    if (bs.NPayloadBytes > 0 && s.SrcEnd <= s.Src)
                        return Constants.StatusSrcEmpty; // Need more source data

                    // Initialize LZVN decoder state
                    LzvnDecoderState dstate = new LzvnDecoderState
                    {
                        SrcPos = s.Src,
                        SrcEnd = s.SrcEnd,
                        DstPos = s.Dst,
                        DstBegin = s.DstBegin,
                        DstEnd = s.DstEnd,
                        DPrev = (int)bs.DPrev,
                        EndOfStream = false
                    };

                    // Limit to payload bytes
                    if (dstate.SrcEnd - s.Src > bs.NPayloadBytes)
                        dstate.SrcEnd = s.Src + (int)bs.NPayloadBytes;

                    // Limit to raw bytes
                    if (dstate.DstEnd - s.Dst > bs.NRawBytes)
                        dstate.DstEnd = s.Dst + (int)bs.NRawBytes;

                    // Run LZVN decoder
                    LzvnDecoder.Decode(ref dstate, s.SrcBuffer, s.DstBuffer);

                    // Update our state
                    int srcUsed = dstate.SrcPos - s.Src;
                    int dstUsed = dstate.DstPos - s.Dst;

                    if (srcUsed > bs.NPayloadBytes || dstUsed > bs.NRawBytes)
                        return Constants.StatusError; // Sanity check

                    s.Src = dstate.SrcPos;
                    s.Dst = dstate.DstPos;
                    bs.NPayloadBytes -= (uint)srcUsed;
                    bs.NRawBytes -= (uint)dstUsed;
                    bs.DPrev = (uint)dstate.DPrev;

                    // Test end of block - successful completion
                    if (bs.NPayloadBytes == 0 && bs.NRawBytes == 0 && dstate.EndOfStream)
                    {
                        s.BlockMagic = Constants.NoBlockMagic;
                        break; // Block done
                    }

                    // Check for destination buffer full
                    if (bs.NRawBytes == 0)
                        return Constants.StatusDstFull;

                    // Check for invalid states
                    if (bs.NPayloadBytes == 0 || dstate.EndOfStream)
                        return Constants.StatusError;

                    // Continue processing
                    break;
                }

                default:
                    return Constants.StatusError; // Invalid magic
            }
        }
    }

    /// <summary>
    /// Decode V1 block header from buffer.
    /// </summary>
    private static LzfseCompressedBlockHeaderV1 DecodeV1Header(ReadOnlySpan<byte> buffer)
    {
        var header = new LzfseCompressedBlockHeaderV1
        {
            Magic = MemoryOperations.Load4(buffer),
            NRawBytes = MemoryOperations.Load4(buffer[4..]),
            NPayloadBytes = MemoryOperations.Load4(buffer[8..]),
            NLiterals = MemoryOperations.Load4(buffer[12..]),
            NMatches = MemoryOperations.Load4(buffer[16..]),
            NLiteralPayloadBytes = MemoryOperations.Load4(buffer[20..]),
            NLmdPayloadBytes = MemoryOperations.Load4(buffer[24..]),
            LiteralBits = (int)MemoryOperations.Load4(buffer[28..]),
            LmdBits = (int)MemoryOperations.Load4(buffer[40..])
        };

        // Decode literal states
        header.LiteralState[0] = MemoryOperations.Load2(buffer[32..]);
        header.LiteralState[1] = MemoryOperations.Load2(buffer[34..]);
        header.LiteralState[2] = MemoryOperations.Load2(buffer[36..]);
        header.LiteralState[3] = MemoryOperations.Load2(buffer[38..]);

        // Decode L, M, D states
        header.LState = MemoryOperations.Load2(buffer[44..]);
        header.MState = MemoryOperations.Load2(buffer[46..]);
        header.DState = MemoryOperations.Load2(buffer[48..]);

        // Decode frequency tables
        int offset = 50;
        for (int i = 0; i < Constants.EncodeLSymbols; i++, offset += 2)
            header.LFreq[i] = MemoryOperations.Load2(buffer[offset..]);

        for (int i = 0; i < Constants.EncodeMSymbols; i++, offset += 2)
            header.MFreq[i] = MemoryOperations.Load2(buffer[offset..]);

        for (int i = 0; i < Constants.EncodeDSymbols; i++, offset += 2)
            header.DFreq[i] = MemoryOperations.Load2(buffer[offset..]);

        for (int i = 0; i < Constants.EncodeLiteralSymbols; i++, offset += 2)
            header.LiteralFreq[i] = MemoryOperations.Load2(buffer[offset..]);

        return header;
    }
}
