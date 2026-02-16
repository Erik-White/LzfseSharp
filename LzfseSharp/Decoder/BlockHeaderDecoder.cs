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

    /// <summary>
    /// Decode a frequency value from bits
    /// </summary>
    private static int DecodeFreqValue(uint bits, out int nbits)
    {
        uint b = bits & 31; // lower 5 bits
        int n = FreqNbitsTable[b];
        nbits = n;

        // Special cases for > 5 bits encoding
        if (n == 8)
            return 8 + (int)((bits >> 4) & 0xf);
        if (n == 14)
            return 24 + (int)((bits >> 4) & 0x3ff);

        // <= 5 bits encoding from table
        return FreqValueTable[b];
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
    /// <param name="outHeader">Decoded V1 header</param>
    /// <param name="headerSize">Actual header size consumed in bytes</param>
    /// <param name="inBytes">Input buffer containing V2 header</param>
    /// <returns>0 on success, -1 on failure</returns>
    public static int DecodeV2ToV1(out LzfseCompressedBlockHeaderV1 outHeader, out int headerSize, ReadOnlySpan<byte> inBytes)
    {
        outHeader = new LzfseCompressedBlockHeaderV1();
        headerSize = 0;

        // Read packed fields
        // Struct: magic(4) + n_raw_bytes(4) + packed_fields[3](24) + freq[]
        ulong v0 = MemoryOperations.Load8(inBytes[8..]);
        ulong v1 = MemoryOperations.Load8(inBytes[16..]);
        ulong v2 = MemoryOperations.Load8(inBytes[24..]);

        outHeader.Magic = Constants.CompressedV1BlockMagic;
        outHeader.NRawBytes = MemoryOperations.Load4(inBytes[4..]);

        // Decode literal state
        outHeader.NLiterals = BitOperations.GetField(v0, 0, 20);
        outHeader.NLiteralPayloadBytes = BitOperations.GetField(v0, 20, 20);
        outHeader.LiteralBits = (int)BitOperations.GetField(v0, 60, 3) - 7;
        outHeader.LiteralState[0] = (ushort)BitOperations.GetField(v1, 0, 10);
        outHeader.LiteralState[1] = (ushort)BitOperations.GetField(v1, 10, 10);
        outHeader.LiteralState[2] = (ushort)BitOperations.GetField(v1, 20, 10);
        outHeader.LiteralState[3] = (ushort)BitOperations.GetField(v1, 30, 10);

        // Decode L,M,D state
        outHeader.NMatches = BitOperations.GetField(v0, 40, 20);
        outHeader.NLmdPayloadBytes = BitOperations.GetField(v1, 40, 20);
        outHeader.LmdBits = (int)BitOperations.GetField(v1, 60, 3) - 7;
        outHeader.LState = (ushort)BitOperations.GetField(v2, 32, 10);
        outHeader.MState = (ushort)BitOperations.GetField(v2, 42, 10);
        outHeader.DState = (ushort)BitOperations.GetField(v2, 52, 10);

        // Total payload size
        outHeader.NPayloadBytes = outHeader.NLiteralPayloadBytes + outHeader.NLmdPayloadBytes;

        // Decode frequency tables
        int srcPos = 32; // Start after packed fields (magic + n_raw_bytes + packed_fields[3])
        uint declaredHeaderSize = BitOperations.GetField(v2, 0, 32);
        int srcEnd = (int)declaredHeaderSize;

        Span<ushort> dstFreq = stackalloc ushort[Constants.EncodeLSymbols + Constants.EncodeMSymbols +
                                                  Constants.EncodeDSymbols + Constants.EncodeLiteralSymbols];
        uint accum = 0;
        int accumNbits = 0;

        // No freq tables?
        if (srcEnd == srcPos)
        {
            outHeader.LFreq.AsSpan().Clear();
            outHeader.MFreq.AsSpan().Clear();
            outHeader.DFreq.AsSpan().Clear();
            outHeader.LiteralFreq.AsSpan().Clear();
            headerSize = srcPos;
            return 0; // OK
        }

        int totalSymbols = Constants.EncodeLSymbols + Constants.EncodeMSymbols +
                           Constants.EncodeDSymbols + Constants.EncodeLiteralSymbols;

        for (int i = 0; i < totalSymbols; i++)
        {
            // Refill accumulator
            while (srcPos < srcEnd && accumNbits + 8 <= 32)
            {
                accum |= (uint)inBytes[srcPos] << accumNbits;
                accumNbits += 8;
                srcPos++;
            }

            // Decode value
            int value = DecodeFreqValue(accum, out int nbits);

            if (nbits > accumNbits)
                return -1; // failed

            dstFreq[i] = (ushort)value;

            // Consume bits
            accum >>= nbits;
            accumNbits -= nbits;
        }

        if (accumNbits >= 8 || srcPos != srcEnd)
            return -1; // should end exactly at header end

        // Copy to output arrays
        dstFreq[..Constants.EncodeLSymbols].CopyTo(outHeader.LFreq);
        dstFreq.Slice(Constants.EncodeLSymbols, Constants.EncodeMSymbols).CopyTo(outHeader.MFreq);
        dstFreq.Slice(Constants.EncodeLSymbols + Constants.EncodeMSymbols, Constants.EncodeDSymbols).CopyTo(outHeader.DFreq);
        dstFreq.Slice(Constants.EncodeLSymbols + Constants.EncodeMSymbols + Constants.EncodeDSymbols, Constants.EncodeLiteralSymbols).CopyTo(outHeader.LiteralFreq);

        headerSize = srcPos;
        return 0; // OK
    }

    /// <summary>
    /// Check block header V1 validity
    /// </summary>
    public static int CheckBlockHeaderV1(ref LzfseCompressedBlockHeaderV1 header)
    {
        int testsResults = 0;

        testsResults |= header.Magic == Constants.CompressedV1BlockMagic ? 0 : (1 << 0);
        testsResults |= header.NLiterals <= Constants.LiteralsPerBlock ? 0 : (1 << 1);
        testsResults |= header.NMatches <= Constants.MatchesPerBlock ? 0 : (1 << 2);
        testsResults |= header.LiteralState[0] < Constants.EncodeLiteralStates ? 0 : (1 << 3);
        testsResults |= header.LiteralState[1] < Constants.EncodeLiteralStates ? 0 : (1 << 4);
        testsResults |= header.LiteralState[2] < Constants.EncodeLiteralStates ? 0 : (1 << 5);
        testsResults |= header.LiteralState[3] < Constants.EncodeLiteralStates ? 0 : (1 << 6);
        testsResults |= header.LState < Constants.EncodeLStates ? 0 : (1 << 7);
        testsResults |= header.MState < Constants.EncodeMStates ? 0 : (1 << 8);
        testsResults |= header.DState < Constants.EncodeDStates ? 0 : (1 << 9);

        int res;
        res = Fse.FseDecoder.CheckFreq(header.LFreq, Constants.EncodeLSymbols, Constants.EncodeLStates);
        testsResults |= res == 0 ? 0 : (1 << 10);
        res = Fse.FseDecoder.CheckFreq(header.MFreq, Constants.EncodeMSymbols, Constants.EncodeMStates);
        testsResults |= res == 0 ? 0 : (1 << 11);
        res = Fse.FseDecoder.CheckFreq(header.DFreq, Constants.EncodeDSymbols, Constants.EncodeDStates);
        testsResults |= res == 0 ? 0 : (1 << 12);
        res = Fse.FseDecoder.CheckFreq(header.LiteralFreq, Constants.EncodeLiteralSymbols, Constants.EncodeLiteralStates);
        testsResults |= res == 0 ? 0 : (1 << 13);

        return testsResults != 0 ? (testsResults | unchecked((int)0x80000000)) : 0;
    }
}
