namespace LzfseSharp.Decoder;

/// <summary>
/// Compressed block header with compressed frequency tables (V2)
/// </summary>
internal struct LzfseCompressedBlockHeaderV2
{
    public uint Magic;
    public uint NRawBytes;
    public ulong[] PackedFields = new ulong[3];
    // Variable size
    public byte[] Freq = new byte[2 * (Constants.EncodeLSymbols + Constants.EncodeMSymbols + Constants.EncodeDSymbols + Constants.EncodeLiteralSymbols)];

    public LzfseCompressedBlockHeaderV2()
    {
    }
}
