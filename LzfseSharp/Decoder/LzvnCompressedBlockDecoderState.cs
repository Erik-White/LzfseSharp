namespace LzfseSharp.Decoder;

/// <summary>
/// LZVN compressed block decoder state
/// </summary>
internal struct LzvnCompressedBlockDecoderState
{
    public uint RawByteCount;
    public uint PayloadByteCount;
    public uint PreviousDistance;

    // A partially expanded literal/match that did not fit in the destination window on
    // the previous decode step. Carried across a destination-full boundary so the LZVN
    // sub-decoder can resume the op instead of re-parsing the (already consumed) opcode.
    // All zero when there is no pending op.
    public nuint PendingLiteralLength;
    public nuint PendingMatchLength;
    public nuint PendingMatchDistance;
}
