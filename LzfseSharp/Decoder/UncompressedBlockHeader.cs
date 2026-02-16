using System.Runtime.InteropServices;

namespace LzfseSharp.Decoder;

/// <summary>
/// Uncompressed block header
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UncompressedBlockHeader
{
    public uint Magic;
    public uint RawByteCount;
}
