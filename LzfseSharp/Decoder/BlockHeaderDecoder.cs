using LzfseSharp.Core;

namespace LzfseSharp.Decoder;

/// <summary>
/// Block header decoding utilities
/// </summary>
internal static class BlockHeaderDecoder
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
    /// Decode a frequency value from bits
    /// </summary>
    private static FreqDecodeResult DecodeFreqValue(uint bits)
    {
        const uint Lower5BitMask = 31;
        const int ExtendedBits8 = 8;
        const int ExtendedBits14 = 14;

        uint tableIndex = bits & Lower5BitMask;
        int numBits = FreqNbitsTable[tableIndex];

        // Special cases for > 5 bits encoding
        if (numBits == ExtendedBits8)
            return new FreqDecodeResult(8 + (int)((bits >> 4) & 0xf), numBits);

        if (numBits == ExtendedBits14)
            return new FreqDecodeResult(24 + (int)((bits >> 4) & 0x3ff), numBits);

        // <= 5 bits encoding from table
        return new FreqDecodeResult(FreqValueTable[tableIndex], numBits);
    }

    /// <summary>
    /// Get header size from V2 header
    /// </summary>
    public static uint GetV2HeaderSize(ReadOnlySpan<byte> headerBytes)
    {
        // Skip magic (4 bytes) to get to packed_fields[0]
        // packed_fields[2] is at offset 4 + 8 + 8 = 20
        // It contains header_size in bits [0:31]
        ulong packed2 = MemoryOperations.Load8(headerBytes[20..]);
        return BitOperations.GetField(packed2, 0, 32);
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
        const int FreqTablesOffset = 32; // Start after packed fields (magic + n_raw_bytes + packed_fields[3])
        const int BitsPerByte = 8;
        const int MaxAccumulatorBits = 32;

        int sourcePosition = FreqTablesOffset;
        uint declaredHeaderSize = BitOperations.GetField(packedField2, 0, 32);
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

        return validationErrors != 0 ? (validationErrors | ErrorFlag) : 0;
    }
}
