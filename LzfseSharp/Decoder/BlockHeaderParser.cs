using LzfseSharp.Core;

namespace LzfseSharp.Decoder;

/// <summary>
/// Parses LZFSE block headers. All reads of magic numbers, raw-byte counts, payload
/// sizes, and V1/V2 structured headers go through this class; the rest of the decoder
/// sees already-validated, structured header values.
/// </summary>
internal static class BlockHeaderParser
{
    private static readonly sbyte[] FreqNbitsTable =
    [
        2, 3, 2, 5, 2, 3, 2, 8, 2, 3, 2, 5, 2, 3, 2, 14,
        2, 3, 2, 5, 2, 3, 2, 8, 2, 3, 2, 5, 2, 3, 2, 14
    ];

    private static readonly sbyte[] FreqValueTable =
    [
        0, 2, 1, 4, 0, 3, 1, -1, 0, 2, 1, 5, 0, 3, 1, -1,
        0, 2, 1, 6, 0, 3, 1, -1, 0, 2, 1, 7, 0, 3, 1, -1
    ];

    private readonly record struct FreqDecodeResult(int Value, int BitsUsed);

    internal readonly record struct V2ToV1DecodeResult(
        LzfseCompressedBlockHeaderV1 Header,
        int HeaderSize,
        int Status);

    /// <summary>
    /// Describes the structural layout of a block as seen by the header scanner:
    /// which block type it is, how much raw (decompressed) output it contributes,
    /// and the total on-wire length including header and payload.
    /// </summary>
    /// <remarks>
    /// <see cref="BlockLength"/> is zero for the end-of-stream block.
    /// </remarks>
    internal readonly record struct BlockLayout(uint Magic, uint RawBytes, long BlockLength);

    public static uint ReadMagic(ReadOnlySpan<byte> src) => MemoryOperations.Load4(src);

    /// <summary>
    /// Reads the raw-byte count from an uncompressed block header. Caller has already
    /// verified the magic and that at least <see cref="Constants.UncompressedBlockHeaderSize"/>
    /// bytes are available.
    /// </summary>
    public static uint ReadUncompressedRawBytes(ReadOnlySpan<byte> src)
        => MemoryOperations.Load4(src[4..]);

    /// <summary>
    /// Reads the raw-byte count and payload size from an LZVN block header. Caller has
    /// already verified the magic and that at least <see cref="Constants.LzvnCompressedBlockHeaderSize"/>
    /// bytes are available.
    /// </summary>
    public static (uint RawBytes, uint PayloadBytes) ReadLzvnHeader(ReadOnlySpan<byte> src)
    {
        uint rawBytes = MemoryOperations.Load4(src[4..]);
        uint payloadBytes = MemoryOperations.Load4(src[8..]);
        return (rawBytes, payloadBytes);
    }

    /// <summary>
    /// Returns the next block's layout (magic, raw-byte contribution, total block length
    /// on the wire). Used by the pre-scan that sizes the output buffer without decoding
    /// payloads. Throws <see cref="InvalidDataException"/> on truncation, invalid magic,
    /// or malformed V2 header — callers that want a status-based API should use the
    /// span-based <see cref="LzfseDecoder.Decompress(System.Span{byte}, System.ReadOnlySpan{byte}, out DecompressStatus)"/>.
    /// </summary>
    public static BlockLayout ReadBlockLayout(ReadOnlySpan<byte> src, int pos)
    {
        if (pos + 4 > src.Length)
            throw new InvalidDataException("LZFSE stream is truncated: missing block magic.");

        uint magic = ReadMagic(src[pos..]);

        if (magic == Constants.EndOfStreamBlockMagic)
            return new BlockLayout(magic, RawBytes: 0, BlockLength: 0);

        // Every remaining block type begins with { magic(4), n_raw_bytes(4), ... }.
        if (pos + 8 > src.Length)
            throw new InvalidDataException("LZFSE stream is truncated: incomplete block header.");

        uint rawBytes = MemoryOperations.Load4(src[(pos + 4)..]);

        long blockLength = magic switch
        {
            Constants.UncompressedBlockMagic => Constants.UncompressedBlockHeaderSize + rawBytes,
            Constants.CompressedLzvnBlockMagic => ReadLzvnBlockLength(src, pos),
            Constants.CompressedV1BlockMagic => ReadV1BlockLength(src, pos),
            Constants.CompressedV2BlockMagic => ReadV2BlockLength(src, pos),
            _ => throw new InvalidDataException($"LZFSE stream has invalid block magic 0x{magic:X8}.")
        };

        return new BlockLayout(magic, rawBytes, blockLength);

        static long ReadLzvnBlockLength(ReadOnlySpan<byte> src, int pos)
        {
            if (pos + Constants.LzvnCompressedBlockHeaderSize > src.Length)
                throw new InvalidDataException("LZFSE stream is truncated: incomplete LZVN block header.");
            uint payloadBytes = MemoryOperations.Load4(src[(pos + 8)..]);
            return Constants.LzvnCompressedBlockHeaderSize + payloadBytes;
        }

        static long ReadV1BlockLength(ReadOnlySpan<byte> src, int pos)
        {
            if (pos + Constants.V1HeaderSize > src.Length)
                throw new InvalidDataException("LZFSE stream is truncated: incomplete V1 block header.");
            uint nLiteralPayload = MemoryOperations.Load4(src[(pos + 20)..]);
            uint nLmdPayload = MemoryOperations.Load4(src[(pos + 24)..]);
            return (long)Constants.V1HeaderSize + nLiteralPayload + nLmdPayload;
        }

        static long ReadV2BlockLength(ReadOnlySpan<byte> src, int pos)
        {
            if (pos + Constants.V2FixedHeaderSize > src.Length)
                throw new InvalidDataException("LZFSE stream is truncated: incomplete V2 block header.");

            // Reuse the real header parser so we apply the same validation (declared
            // header size bounds, freq-table bit stream) that DecodeInternal will.
            var result = DecodeV2ToV1(src[pos..]);
            if (result.Status != 0)
                throw new InvalidDataException("LZFSE V2 block header is malformed.");

            return (long)result.HeaderSize + result.Header.NLiteralPayloadBytes + result.Header.NLmdPayloadBytes;
        }
    }

    /// <summary>
    /// Decode a V1 block header from a buffer positioned at the start of the header.
    /// </summary>
    public static LzfseCompressedBlockHeaderV1 DecodeV1(ReadOnlySpan<byte> buffer)
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

    /// <summary>
    /// Decode a frequency value from bits
    /// </summary>
    private static FreqDecodeResult DecodeFreqValue(uint bits)
    {
        const uint Lower5BitMask = 31;
        const int ExtendedBits8 = 8;
        const int ExtendedBits14 = 14;

        uint tableIndex = bits & Lower5BitMask;
        int numBits = FreqNbitsTable[tableIndex];

        return numBits switch
        {
            ExtendedBits8 => new FreqDecodeResult(8 + (int)((bits >> 4) & 0xf), numBits),
            ExtendedBits14 => new FreqDecodeResult(24 + (int)((bits >> 4) & 0x3ff), numBits),
            _ => new FreqDecodeResult(FreqValueTable[tableIndex], numBits) // <= 5 bits encoding from table
        };
    }

    /// <summary>
    /// Decode V2 header to V1 format
    /// </summary>
    /// <param name="inBytes">Input buffer containing V2 header</param>
    /// <returns>Result containing decoded header, header size, and status (0 on success, -1 on failure)</returns>
    public static V2ToV1DecodeResult DecodeV2ToV1(ReadOnlySpan<byte> inBytes)
    {
        LzfseCompressedBlockHeaderV1 outHeader = new LzfseCompressedBlockHeaderV1();
        int headerSize = 0;

        // Read packed fields
        // Struct: magic(4) + n_raw_bytes(4) + packed_fields[3](24) + freq[]
        const int PackedField0Offset = 8;
        const int PackedField1Offset = 16;
        const int PackedField2Offset = 24;

        ulong packedField0 = MemoryOperations.Load8(inBytes[PackedField0Offset..]);
        ulong packedField1 = MemoryOperations.Load8(inBytes[PackedField1Offset..]);
        ulong packedField2 = MemoryOperations.Load8(inBytes[PackedField2Offset..]);

        outHeader.Magic = Constants.CompressedV1BlockMagic;
        outHeader.NRawBytes = MemoryOperations.Load4(inBytes[4..]);

        // Decode literal state
        outHeader.NLiterals = BitOperations.GetField(packedField0, 0, 20);
        outHeader.NLiteralPayloadBytes = BitOperations.GetField(packedField0, 20, 20);
        outHeader.LiteralBits = (int)BitOperations.GetField(packedField0, 60, 3) - 7;
        outHeader.LiteralState[0] = (ushort)BitOperations.GetField(packedField1, 0, 10);
        outHeader.LiteralState[1] = (ushort)BitOperations.GetField(packedField1, 10, 10);
        outHeader.LiteralState[2] = (ushort)BitOperations.GetField(packedField1, 20, 10);
        outHeader.LiteralState[3] = (ushort)BitOperations.GetField(packedField1, 30, 10);

        // Decode L,M,D state
        outHeader.NMatches = BitOperations.GetField(packedField0, 40, 20);
        outHeader.NLmdPayloadBytes = BitOperations.GetField(packedField1, 40, 20);
        outHeader.LmdBits = (int)BitOperations.GetField(packedField1, 60, 3) - 7;
        outHeader.LState = (ushort)BitOperations.GetField(packedField2, 32, 10);
        outHeader.MState = (ushort)BitOperations.GetField(packedField2, 42, 10);
        outHeader.DState = (ushort)BitOperations.GetField(packedField2, 52, 10);

        // Total payload size
        outHeader.NPayloadBytes = outHeader.NLiteralPayloadBytes + outHeader.NLmdPayloadBytes;

        // Decode frequency tables
        // Start after magic + n_raw_bytes + packed_fields[3].
        const int FreqTablesOffset = Constants.V2FixedHeaderSize;
        const int BitsPerByte = 8;
        const int MaxAccumulatorBits = 32;

        int sourcePosition = FreqTablesOffset;
        uint declaredHeaderSize = BitOperations.GetField(packedField2, 0, 32);

        // The declared header size is untrusted — it must cover at least the fixed
        // header (FreqTablesOffset bytes) and must not extend past the bytes actually
        // supplied to us. Without this check a crafted stream with an inflated
        // declaredHeaderSize causes an IndexOutOfRangeException in the freq loop
        // below rather than a clean decode error.
        if (declaredHeaderSize < FreqTablesOffset || declaredHeaderSize > (uint)inBytes.Length)
            return new V2ToV1DecodeResult(outHeader, 0, -1);

        int sourceEnd = (int)declaredHeaderSize;

        int totalSymbols = Constants.EncodeLSymbols + Constants.EncodeMSymbols +
                           Constants.EncodeDSymbols + Constants.EncodeLiteralSymbols;

        Span<ushort> allFrequencies = stackalloc ushort[totalSymbols];
        uint bitAccumulator = 0;
        int accumulatedBits = 0;

        // No frequency tables?
        if (sourceEnd == sourcePosition)
        {
            outHeader.LFreq.AsSpan().Clear();
            outHeader.MFreq.AsSpan().Clear();
            outHeader.DFreq.AsSpan().Clear();
            outHeader.LiteralFreq.AsSpan().Clear();
            headerSize = sourcePosition;
            return new V2ToV1DecodeResult(outHeader, headerSize, 0);
        }

        for (int i = 0; i < totalSymbols; i++)
        {
            // Refill accumulator
            while (sourcePosition < sourceEnd && accumulatedBits + BitsPerByte <= MaxAccumulatorBits)
            {
                bitAccumulator |= (uint)inBytes[sourcePosition] << accumulatedBits;
                accumulatedBits += BitsPerByte;
                sourcePosition++;
            }

            // Decode value
            FreqDecodeResult decodeResult = DecodeFreqValue(bitAccumulator);

            if (decodeResult.BitsUsed > accumulatedBits)
                return new V2ToV1DecodeResult(outHeader, headerSize, -1); // Failed - not enough bits

            allFrequencies[i] = (ushort)decodeResult.Value;

            // Consume bits
            bitAccumulator >>= decodeResult.BitsUsed;
            accumulatedBits -= decodeResult.BitsUsed;
        }

        if (accumulatedBits >= BitsPerByte || sourcePosition != sourceEnd)
            return new V2ToV1DecodeResult(outHeader, headerSize, -1); // Should end exactly at header end

        // Copy to output arrays
        allFrequencies[..Constants.EncodeLSymbols].CopyTo(outHeader.LFreq);
        allFrequencies.Slice(Constants.EncodeLSymbols, Constants.EncodeMSymbols).CopyTo(outHeader.MFreq);
        allFrequencies.Slice(Constants.EncodeLSymbols + Constants.EncodeMSymbols, Constants.EncodeDSymbols).CopyTo(outHeader.DFreq);
        allFrequencies.Slice(Constants.EncodeLSymbols + Constants.EncodeMSymbols + Constants.EncodeDSymbols, Constants.EncodeLiteralSymbols).CopyTo(outHeader.LiteralFreq);

        headerSize = sourcePosition;
        return new V2ToV1DecodeResult(outHeader, headerSize, 0);
    }

    /// <summary>
    /// Check block header V1 validity
    /// </summary>
    public static int CheckBlockHeaderV1(ref LzfseCompressedBlockHeaderV1 header)
    {
        const int ErrorFlag = unchecked((int)0x80000000);
        int validationErrors = 0;

        if (header.Magic != Constants.CompressedV1BlockMagic)
            validationErrors |= 1 << 0;

        if (header.NLiterals > Constants.LiteralsPerBlock)
            validationErrors |= 1 << 1;

        if (header.NMatches > Constants.MatchesPerBlock)
            validationErrors |= 1 << 2;

        if (header.LiteralState[0] >= Constants.EncodeLiteralStates)
            validationErrors |= 1 << 3;

        if (header.LiteralState[1] >= Constants.EncodeLiteralStates)
            validationErrors |= 1 << 4;

        if (header.LiteralState[2] >= Constants.EncodeLiteralStates)
            validationErrors |= 1 << 5;

        if (header.LiteralState[3] >= Constants.EncodeLiteralStates)
            validationErrors |= 1 << 6;

        if (header.LState >= Constants.EncodeLStates)
            validationErrors |= 1 << 7;

        if (header.MState >= Constants.EncodeMStates)
            validationErrors |= 1 << 8;

        if (header.DState >= Constants.EncodeDStates)
            validationErrors |= 1 << 9;

        if (Fse.FseDecoder.CheckFreq(header.LFreq, Constants.EncodeLSymbols, Constants.EncodeLStates) != 0)
            validationErrors |= 1 << 10;

        if (Fse.FseDecoder.CheckFreq(header.MFreq, Constants.EncodeMSymbols, Constants.EncodeMStates) != 0)
            validationErrors |= 1 << 11;

        if (Fse.FseDecoder.CheckFreq(header.DFreq, Constants.EncodeDSymbols, Constants.EncodeDStates) != 0)
            validationErrors |= 1 << 12;

        if (Fse.FseDecoder.CheckFreq(header.LiteralFreq, Constants.EncodeLiteralSymbols, Constants.EncodeLiteralStates) != 0)
            validationErrors |= 1 << 13;

        // NLiterals must be a multiple of 4: the literal decoder reads 4 at a time.
        if ((header.NLiterals & 3) != 0)
            validationErrors |= 1 << 14;

        // NPayloadBytes is redundant in the stream (NLiteralPayloadBytes + NLmdPayloadBytes),
        // but when supplied it must agree with the two component fields.
        if (header.NPayloadBytes != header.NLiteralPayloadBytes + header.NLmdPayloadBytes)
            validationErrors |= 1 << 15;

        return validationErrors != 0 ? (validationErrors | ErrorFlag) : 0;
    }
}
