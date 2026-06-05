namespace LzfseSharp.Decoder;

/// <summary>
/// Main LZFSE decoder state. Holds only the positional and per-block bookkeeping that
/// must persist across decode steps; the source and destination buffers are passed in
/// as spans on each call so this struct can be stored as a field by the streaming driver
/// (a <c>ref struct</c> holding spans could not be).
/// </summary>
internal struct LzfseDecoderState
{
    // An offset into the source buffer supplied to the decode methods.
    public int SourcePosition;
    public int SourceEnd;

    // An offset into the destination (window) buffer supplied to the decode methods.
    public int DestinationPosition;
    public int DestinationEnd;

    // Stream state
    public bool EndOfStream;
    public uint BlockMagic;

    // Block-specific decoder states
    public LzfseCompressedBlockDecoderState CompressedLzfseBlockState;
    public LzvnCompressedBlockDecoderState CompressedLzvnBlockState;
    public UncompressedBlockDecoderState UncompressedBlockState;
}
