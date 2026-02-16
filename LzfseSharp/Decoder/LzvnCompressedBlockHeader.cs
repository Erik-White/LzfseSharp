using System.Runtime.InteropServices;

namespace LzfseSharp.Decoder;

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
