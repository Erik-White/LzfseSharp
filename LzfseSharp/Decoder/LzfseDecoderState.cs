namespace LzfseSharp.Decoder;

/// <summary>
/// Main LZFSE decoder state
/// </summary>
internal ref struct LzfseDecoderState
{
    // Source buffer pointers
    public int SourcePosition;
    public int SourceStart;
    public int SourceEnd;

    // Destination buffer pointers
    public int DestinationPosition;
    public int DestinationStart;
    public int DestinationEnd;

    // Stream state
    public bool EndOfStream;
    public uint BlockMagic;

    // Block-specific decoder states
    public LzfseCompressedBlockDecoderState CompressedLzfseBlockState;
    public LzvnCompressedBlockDecoderState CompressedLzvnBlockState;
    public UncompressedBlockDecoderState UncompressedBlockState;

    // Buffers
    public ReadOnlySpan<byte> SourceBuffer;
    public Span<byte> DestinationBuffer;
}
