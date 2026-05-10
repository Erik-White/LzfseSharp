namespace LzfseSharp.Decoder;

/// <summary>
/// Main LZFSE decoder state
/// </summary>
internal ref struct LzfseDecoderState
{
    // An offset into <see cref="SourceBuffer"/>
    public int SourcePosition;
    public int SourceEnd;

    // An offset into <see cref="DestinationBuffer"/>
    public int DestinationPosition;
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
