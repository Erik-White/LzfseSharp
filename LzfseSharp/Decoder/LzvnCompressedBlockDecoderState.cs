namespace LzfseSharp.Decoder;

/// <summary>
/// LZVN compressed block decoder state
/// </summary>
internal struct LzvnCompressedBlockDecoderState
{
    public uint NRawBytes;
    public uint NPayloadBytes;
    public uint DPrev;
}
