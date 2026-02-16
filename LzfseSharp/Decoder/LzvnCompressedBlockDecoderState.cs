namespace LzfseSharp.Decoder;

/// <summary>
/// LZVN compressed block decoder state
/// </summary>
internal struct LzvnCompressedBlockDecoderState
{
    public uint RawByteCount;
    public uint PayloadByteCount;
    public uint PreviousDistance;
}
