using LzfseSharp.Decoder;
using LzfseSharp.Fse;
using LzfseSharp.Lzvn;

namespace LzfseSharp;

/// <summary>
/// Main LZFSE decoder providing the public API for decompressing LZFSE-compressed data.
/// </summary>
/// <remarks>
/// The decoder is fully stream based: it reads compressed input from a source
/// <see cref="Stream"/> one block at a time and writes decompressed output to a
/// destination <see cref="Stream"/> through a bounded sliding window. Memory use is
/// independent of the (de)compressed length — it is governed by the largest single
/// block in the input and the fixed-size output window.
/// </remarks>
public static class LzfseDecoder
{
    /// <summary>
    /// Decompresses LZFSE-compressed data, reading from <paramref name="source"/> and
    /// writing the decompressed output to <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">Stream positioned at the start of the LZFSE-compressed data.</param>
    /// <param name="destination">Stream that the decompressed bytes are written to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="destination"/> is null.</exception>
    /// <exception cref="EndOfStreamException">The source ended before the end-of-stream marker was reached.</exception>
    /// <exception cref="InvalidDataException">The compressed data is malformed.</exception>
    public static void Decompress(Stream source, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        SourceBuffer sourceBuffer = new();
        OutputWindow window = new();
        LzfseDecoderState state = default;
        state.BlockMagic = Constants.NoBlockMagic;
        state.CompressedLzfseBlockState = LzfseCompressedBlockDecoderState.Rent();

        try
        {
            while (true)
            {
                int status = DecodePass(ref state, sourceBuffer, window);

                switch (status)
                {
                    case Constants.StatusOk:
                        window.FlushRemaining(destination);
                        return;

                    case Constants.StatusSrcEmpty:
                        sourceBuffer.Position = state.SourcePosition;
                        bool gotMore = sourceBuffer.Fill(source);
                        state.SourcePosition = sourceBuffer.Position;
                        if (!gotMore)
                            throw new EndOfStreamException("LZFSE stream ended before the end-of-stream marker.");
                        break;

                    case Constants.StatusDstFull:
                    {
                        int freed = window.Flush(destination);
                        // A legitimate destination-full only happens when the window has
                        // filled past RetainBytes, so the flush always frees space. Zero
                        // freed bytes means the decoder yielded without progress on a block
                        // whose declared size it cannot satisfy — a malformed stream.
                        if (freed == 0)
                            throw new InvalidDataException("The compressed data is not valid LZFSE.");
                        state.DestinationPosition -= freed;
                        break;
                    }

                    default:
                        throw new InvalidDataException("The compressed data is not valid LZFSE.");
                }
            }
        }
        finally
        {
            state.CompressedLzfseBlockState.Return();
            window.Return();
            sourceBuffer.Return();
        }
    }

    /// <summary>
    /// Asynchronously decompresses LZFSE-compressed data, reading from
    /// <paramref name="source"/> and writing the decompressed output to
    /// <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">Stream positioned at the start of the LZFSE-compressed data.</param>
    /// <param name="destination">Stream that the decompressed bytes are written to.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="destination"/> is null.</exception>
    /// <exception cref="EndOfStreamException">The source ended before the end-of-stream marker was reached.</exception>
    /// <exception cref="InvalidDataException">The compressed data is malformed.</exception>
    public static async ValueTask DecompressAsync(Stream source, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        SourceBuffer sourceBuffer = new();
        OutputWindow window = new();
        LzfseDecoderState state = default;
        state.BlockMagic = Constants.NoBlockMagic;
        state.CompressedLzfseBlockState = LzfseCompressedBlockDecoderState.Rent();

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The decode pass itself is synchronous: it operates only on the already
                // resident source/window buffers and never spans an await. I/O (reading
                // more source, flushing the window) happens between passes, where awaiting
                // is safe because no Span is held across it.
                int status = DecodePass(ref state, sourceBuffer, window);

                switch (status)
                {
                    case Constants.StatusOk:
                        await window.FlushRemainingAsync(destination, cancellationToken).ConfigureAwait(false);
                        return;

                    case Constants.StatusSrcEmpty:
                        sourceBuffer.Position = state.SourcePosition;
                        bool gotMore = await sourceBuffer.FillAsync(source, cancellationToken).ConfigureAwait(false);
                        state.SourcePosition = sourceBuffer.Position;
                        if (!gotMore)
                            throw new EndOfStreamException("LZFSE stream ended before the end-of-stream marker.");
                        break;

                    case Constants.StatusDstFull:
                    {
                        int freed = await window.FlushAsync(destination, cancellationToken).ConfigureAwait(false);
                        // See the synchronous driver: a zero-progress destination-full means
                        // the block declares more output than it can produce — malformed.
                        if (freed == 0)
                            throw new InvalidDataException("The compressed data is not valid LZFSE.");
                        state.DestinationPosition -= freed;
                        break;
                    }

                    default:
                        throw new InvalidDataException("The compressed data is not valid LZFSE.");
                }
            }
        }
        finally
        {
            state.CompressedLzfseBlockState.Return();
            window.Return();
            sourceBuffer.Return();
        }
    }

    /// <summary>
    /// Runs one synchronous decode pass over the resident buffers. Factored out so the
    /// async driver can keep all <see cref="Span{T}"/> use inside a non-async method.
    /// </summary>
    private static int DecodePass(ref LzfseDecoderState state, SourceBuffer sourceBuffer, OutputWindow window)
    {
        state.SourceEnd = sourceBuffer.Available;
        state.DestinationEnd = window.WritableEnd;
        int status = DecodeInternal(ref state, sourceBuffer.Data, window.Buffer);
        window.Advance(state.DestinationPosition);
        return status;
    }

    /// <summary>
    /// Internal decoding function that processes blocks in the stream until it either
    /// finishes, needs more source, or fills the destination window.
    /// </summary>
    private static int DecodeInternal(ref LzfseDecoderState s, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        while (true)
        {
            // Are we inside a block?
            switch (s.BlockMagic)
            {
                case Constants.NoBlockMagic:
                {
                    // We need at least 4 bytes of magic number to identify next block
                    if (s.SourcePosition + 4 > s.SourceEnd)
                        return Constants.StatusSrcEmpty; // Source truncated

                    uint magic = BlockHeaderParser.ReadMagic(source[s.SourcePosition..]);

                    switch (magic)
                    {
                        case Constants.EndOfStreamBlockMagic:
                            // End of stream block
                            s.SourcePosition += 4;
                            s.EndOfStream = true;
                            return Constants.StatusOk; // Done

                        case Constants.UncompressedBlockMagic:
                        {
                            if (s.SourcePosition + Constants.UncompressedBlockHeaderSize > s.SourceEnd)
                                return Constants.StatusSrcEmpty; // Source truncated

                            ref UncompressedBlockDecoderState bs = ref s.UncompressedBlockState;
                            bs.RawByteCount = BlockHeaderParser.ReadUncompressedRawBytes(source[s.SourcePosition..]);
                            s.SourcePosition += Constants.UncompressedBlockHeaderSize;
                            s.BlockMagic = magic;
                            break;
                        }

                        case Constants.CompressedLzvnBlockMagic:
                        {
                            if (s.SourcePosition + Constants.LzvnCompressedBlockHeaderSize > s.SourceEnd)
                                return Constants.StatusSrcEmpty; // Source truncated

                            (uint rawBytes, uint payloadBytes) = BlockHeaderParser.ReadLzvnHeader(source[s.SourcePosition..]);

                            // Reject an implausibly large declared payload
                            if (payloadBytes > Constants.MaxBlockPayloadBytes)
                                return Constants.StatusError;

                            // Require the whole payload to be resident before decoding so the
                            // LZVN sub-decoder is never limited by source truncation mid-block,
                            // its only stopping reason then is the destination window filling.
                            if ((long)s.SourcePosition + Constants.LzvnCompressedBlockHeaderSize + payloadBytes > s.SourceEnd)
                                return Constants.StatusSrcEmpty; // Need whole encoded block

                            ref LzvnCompressedBlockDecoderState bs = ref s.CompressedLzvnBlockState;
                            bs.RawByteCount = rawBytes;
                            bs.PayloadByteCount = payloadBytes;
                            bs.PreviousDistance = 0;
                            bs.PendingLiteralLength = 0;
                            bs.PendingMatchLength = 0;
                            bs.PendingMatchDistance = 0;
                            s.SourcePosition += Constants.LzvnCompressedBlockHeaderSize;
                            s.BlockMagic = magic;
                            break;
                        }

                        case Constants.CompressedV1BlockMagic:
                        case Constants.CompressedV2BlockMagic:
                        {
                            // LZFSE compressed blocks (V1 or V2)
                            LzfseCompressedBlockHeaderV1 header1;
                            uint headerSize;

                            if (magic == Constants.CompressedV2BlockMagic)
                            {
                                // DecodeV2ToV1 loads packed_fields[2] at offset 24..31, so the fixed-header
                                // guard is the minimum safe bound even when the declared header size
                                // (read from packed_fields[2]) is smaller.
                                if (s.SourcePosition + Constants.V2FixedHeaderSize > s.SourceEnd)
                                    return Constants.StatusSrcEmpty; // Source truncated

                                var decodeResult = BlockHeaderParser.DecodeV2ToV1(source[s.SourcePosition..]);
                                if (decodeResult.Status != 0)
                                    return Constants.StatusError; // Failed

                                header1 = decodeResult.Header;
                                headerSize = (uint)decodeResult.HeaderSize;
                            }
                            else
                            {
                                if (s.SourcePosition + Constants.V1HeaderSize > s.SourceEnd)
                                    return Constants.StatusSrcEmpty; // Source truncated

                                header1 = BlockHeaderParser.DecodeV1(source[s.SourcePosition..]);
                                headerSize = (uint)Constants.V1HeaderSize;
                            }

                            // Reject implausibly large declared payloads before requiring them
                            // resident: otherwise a malformed header drives the source buffer to
                            // grow without bound chasing bytes that will never arrive.
                            if (header1.NLiteralPayloadBytes > Constants.MaxBlockPayloadBytes ||
                                header1.NLmdPayloadBytes > Constants.MaxBlockPayloadBytes)
                                return Constants.StatusError;

                            // We require the header + entire encoded block to be present in source
                            // during the entire block decoding
                            if ((long)s.SourcePosition + headerSize + header1.NLiteralPayloadBytes + header1.NLmdPayloadBytes > s.SourceEnd)
                                return Constants.StatusSrcEmpty; // Need all encoded block

                            // Sanity checks
                            if (BlockHeaderParser.CheckBlockHeaderV1(ref header1) != 0)
                                return Constants.StatusError;

                            // Skip header
                            s.SourcePosition += (int)headerSize;

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
                            if (DecodeLiterals(ref s, ref bs, ref header1, source) != 0)
                                return Constants.StatusError;

                            // Skip literal payload
                            s.SourcePosition += (int)header1.NLiteralPayloadBytes;

                            // Initialize the L,M,D decode stream, do not start decoding matches yet
                            if (InitializeLmdStream(ref s, ref bs, ref header1, source) != 0)
                                return Constants.StatusError;

                            s.BlockMagic = magic;
                            break;
                        }

                        default:
                            // Invalid magic number
                            return Constants.StatusError;
                    }
                    break;
                }

                case Constants.UncompressedBlockMagic:
                {
                    ref UncompressedBlockDecoderState bs = ref s.UncompressedBlockState;

                    // Compute the size (in bytes) of the data that we will actually copy.
                    // This size is minimum(bs.n_raw_bytes, space in src, space in dst).
                    uint copySize = bs.RawByteCount; // Bytes left to copy
                    if (copySize == 0)
                    {
                        s.BlockMagic = Constants.NoBlockMagic;
                        break; // End of block
                    }

                    if (s.SourceEnd <= s.SourcePosition)
                        return Constants.StatusSrcEmpty; // Need more source data

                    int srcSpace = s.SourceEnd - s.SourcePosition;
                    if (copySize > srcSpace)
                        copySize = (uint)srcSpace; // Limit to source data (> 0)

                    if (s.DestinationEnd <= s.DestinationPosition)
                        return Constants.StatusDstFull; // Need more destination capacity

                    int dstSpace = s.DestinationEnd - s.DestinationPosition;
                    if (copySize > dstSpace)
                        copySize = (uint)dstSpace; // Limit to destination capacity (> 0)

                    // Copy the data (we always have copySize > 0 here)
                    source.Slice(s.SourcePosition, (int)copySize).CopyTo(destination[s.DestinationPosition..]);
                    s.SourcePosition += (int)copySize;
                    s.DestinationPosition += (int)copySize;
                    bs.RawByteCount -= copySize;

                    break;
                }

                case Constants.CompressedV1BlockMagic:
                case Constants.CompressedV2BlockMagic:
                {
                    ref LzfseCompressedBlockDecoderState bs = ref s.CompressedLzfseBlockState;

                    // Require the entire LMD payload to be in source
                    if (s.SourceEnd <= s.SourcePosition || bs.NLmdPayloadBytes > (uint)(s.SourceEnd - s.SourcePosition))
                        return Constants.StatusSrcEmpty;

                    int status = LzfseBlockDecoder.DecodeLmd(ref s, source, destination);
                    if (status != Constants.StatusOk)
                        return status;

                    s.BlockMagic = Constants.NoBlockMagic;
                    s.SourcePosition += (int)bs.NLmdPayloadBytes; // To next block
                    break;
                }

                case Constants.CompressedLzvnBlockMagic:
                {
                    ref LzvnCompressedBlockDecoderState bs = ref s.CompressedLzvnBlockState;

                    if (bs.PayloadByteCount > 0 && s.SourceEnd <= s.SourcePosition)
                        return Constants.StatusSrcEmpty; // Need more source data

                    // Initialize LZVN decoder state, restoring any partially expanded op
                    // carried across a destination-window flush.
                    LzvnDecoderState dstate = new LzvnDecoderState
                    {
                        SourcePosition = s.SourcePosition,
                        SourceEnd = s.SourceEnd,
                        DestinationPosition = s.DestinationPosition,
                        DestinationStart = 0,
                        DestinationEnd = s.DestinationEnd,
                        PreviousDistance = (int)bs.PreviousDistance,
                        LiteralLength = bs.PendingLiteralLength,
                        MatchLength = bs.PendingMatchLength,
                        MatchDistance = bs.PendingMatchDistance,
                        EndOfStream = false
                    };

                    // Limit to payload bytes
                    if (dstate.SourceEnd - s.SourcePosition > bs.PayloadByteCount)
                        dstate.SourceEnd = s.SourcePosition + (int)bs.PayloadByteCount;

                    // Limit to raw bytes
                    if (dstate.DestinationEnd - s.DestinationPosition > bs.RawByteCount)
                        dstate.DestinationEnd = s.DestinationPosition + (int)bs.RawByteCount;

                    // Run LZVN decoder
                    LzvnDecoder.Decode(ref dstate, source, destination);

                    // Update our state
                    int srcUsed = dstate.SourcePosition - s.SourcePosition;
                    int dstUsed = dstate.DestinationPosition - s.DestinationPosition;

                    if (srcUsed > bs.PayloadByteCount || dstUsed > bs.RawByteCount)
                        return Constants.StatusError; // Sanity check

                    s.SourcePosition = dstate.SourcePosition;
                    s.DestinationPosition = dstate.DestinationPosition;
                    bs.PayloadByteCount -= (uint)srcUsed;
                    bs.RawByteCount -= (uint)dstUsed;
                    bs.PreviousDistance = (uint)dstate.PreviousDistance;
                    bs.PendingLiteralLength = dstate.LiteralLength;
                    bs.PendingMatchLength = dstate.MatchLength;
                    bs.PendingMatchDistance = dstate.MatchDistance;

                    // LZVN detected an invalid match distance or other semantic error.
                    // This must surface as Malformed, not DestinationFull (which is the
                    // default classification for "decode stopped mid-block").
                    if (dstate.Malformed)
                        return Constants.StatusError;

                    // Test end of block - successful completion
                    if (bs.PayloadByteCount == 0 && bs.RawByteCount == 0 && dstate.EndOfStream)
                    {
                        s.BlockMagic = Constants.NoBlockMagic;
                        break; // Block done
                    }

                    // Check for invalid state
                    if (bs.PayloadByteCount == 0 || bs.RawByteCount == 0 || dstate.EndOfStream)
                        return Constants.StatusError;

                    // Block not done, state valid — need more destination space
                    return Constants.StatusDstFull;
                }

                default:
                    return Constants.StatusError; // Invalid magic
            }
        }
    }

    /// <summary>
    /// Decode literals from the block header
    /// </summary>
    private static int DecodeLiterals(
        ref LzfseDecoderState s,
        ref LzfseCompressedBlockDecoderState bs,
        ref LzfseCompressedBlockHeaderV1 header1,
        ReadOnlySpan<byte> source)
    {
        FseInStream inStream = default;
        const int bufStart = 0;
        int literalPayloadEnd = s.SourcePosition + (int)header1.NLiteralPayloadBytes;
        int buf = literalPayloadEnd; // Read bits backwards from the end

        var initResult = inStream.Init(header1.LiteralBits, buf, bufStart, source);
        if (initResult.Status != 0)
            return -1;
        buf = initResult.BufferPtr;

        ushort state0 = header1.LiteralState[0];
        ushort state1 = header1.LiteralState[1];
        ushort state2 = header1.LiteralState[2];
        ushort state3 = header1.LiteralState[3];

        // Decode literals 4 at a time (n_literals is multiple of 4)
        for (uint i = 0; i < header1.NLiterals; i += 4)
        {
            var flushResult = inStream.Flush(buf, bufStart, source);
            if (flushResult.Status != 0)
                return -1;
            buf = flushResult.BufferPtr;

            bs.Literals[i + 0] = FseDecoder.Decode(ref state0, bs.LiteralDecoder, ref inStream);
            bs.Literals[i + 1] = FseDecoder.Decode(ref state1, bs.LiteralDecoder, ref inStream);
            bs.Literals[i + 2] = FseDecoder.Decode(ref state2, bs.LiteralDecoder, ref inStream);
            bs.Literals[i + 3] = FseDecoder.Decode(ref state3, bs.LiteralDecoder, ref inStream);
        }

        bs.CurrentLiteralPos = 0;
        return 0;
    }

    /// <summary>
    /// Initialize the L,M,D decode stream
    /// </summary>
    private static int InitializeLmdStream(
        ref LzfseDecoderState s,
        ref LzfseCompressedBlockDecoderState bs,
        ref LzfseCompressedBlockHeaderV1 header1,
        ReadOnlySpan<byte> source)
    {
        FseInStream inStream = default;
        // Read bits backwards from the end
        int buf = s.SourcePosition + (int)header1.NLmdPayloadBytes;

        var initResult = inStream.Init(header1.LmdBits, buf, s.SourcePosition, source);
        if (initResult.Status != 0)
            return -1;
        buf = initResult.BufferPtr;

        bs.LState = header1.LState;
        bs.MState = header1.MState;
        bs.DState = header1.DState;
        bs.LmdInBuf = buf - s.SourcePosition;
        bs.LValue = bs.MValue = 0;
        // Initialize D to an illegal value so we can't erroneously use
        // an uninitialized "previous" value
        bs.DValue = -1;
        bs.LmdInStream = inStream;
        return 0;
    }
}
