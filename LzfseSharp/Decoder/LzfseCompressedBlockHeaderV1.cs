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
    public ushort[] LiteralState = new ushort[4];
    public int LmdBits;
    public ushort LState;
    public ushort MState;
    public ushort DState;

    // Frequency tables
    public ushort[] LFreq = new ushort[Constants.EncodeLSymbols];
    public ushort[] MFreq = new ushort[Constants.EncodeMSymbols];
    public ushort[] DFreq = new ushort[Constants.EncodeDSymbols];
    public ushort[] LiteralFreq = new ushort[Constants.EncodeLiteralSymbols];

    public LzfseCompressedBlockHeaderV1()
    {
    }
}
