namespace LzfseSharp.Decoder;

/// <summary>
/// Compressed block header with uncompressed frequency tables (V1)
/// </summary>
internal struct LzfseCompressedBlockHeaderV1
{
    public uint Magic;
    public uint NRawBytes;
    public uint NPayloadBytes;
    public uint NLiterals;
    public uint NMatches;
    public uint NLiteralPayloadBytes;
    public uint NLmdPayloadBytes;

    // Final encoder states
    public int LiteralBits;
    public ushort[] LiteralState;  // [4]
    public int LmdBits;
    public ushort LState;
    public ushort MState;
    public ushort DState;

    // Frequency tables
    public ushort[] LFreq;       // [ENCODE_L_SYMBOLS]
    public ushort[] MFreq;       // [ENCODE_M_SYMBOLS]
    public ushort[] DFreq;       // [ENCODE_D_SYMBOLS]
    public ushort[] LiteralFreq; // [ENCODE_LITERAL_SYMBOLS]

    public LzfseCompressedBlockHeaderV1()
    {
        LiteralState = new ushort[4];
        LFreq = new ushort[Constants.EncodeLSymbols];
        MFreq = new ushort[Constants.EncodeMSymbols];
        DFreq = new ushort[Constants.EncodeDSymbols];
        LiteralFreq = new ushort[Constants.EncodeLiteralSymbols];
    }
}
