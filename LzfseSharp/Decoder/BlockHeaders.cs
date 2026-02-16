using System.Runtime.InteropServices;

namespace LzfseSharp.Decoder;

/// <summary>
/// Uncompressed block header
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UncompressedBlockHeader
{
    public uint Magic;
    public uint NRawBytes;
}

/// <summary>
/// LZVN compressed block header
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct LzvnCompressedBlockHeader
{
    public uint Magic;
    public uint NRawBytes;
    public uint NPayloadBytes;
}

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

/// <summary>
/// Compressed block header with compressed frequency tables (V2)
/// </summary>
internal struct LzfseCompressedBlockHeaderV2
{
    public uint Magic;
    public uint NRawBytes;
    public ulong[] PackedFields; // [3]
    public byte[] Freq;  // Variable size

    public LzfseCompressedBlockHeaderV2()
    {
        PackedFields = new ulong[3];
        Freq = new byte[2 * (Constants.EncodeLSymbols + Constants.EncodeMSymbols +
                             Constants.EncodeDSymbols + Constants.EncodeLiteralSymbols)];
    }
}
