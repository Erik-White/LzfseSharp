namespace LzfseSharp.Decoder;

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
