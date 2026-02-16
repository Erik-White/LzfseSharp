using System.Runtime.InteropServices;

namespace LzfseSharp.Fse;

/// <summary>
/// Entry for one state in the FSE decoder table
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct FseDecoderEntry
{
    /// <summary>
    /// Number of bits to read
    /// </summary>
    public sbyte K;

    /// <summary>
    /// Emitted symbol
    /// </summary>
    public byte Symbol;

    /// <summary>
    /// Signed increment used to compute next state (+bias)
    /// </summary>
    public short Delta;
}
