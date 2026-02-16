using LzfseSharp.Fse;

namespace LzfseSharp.Decoder;

/// <summary>
/// Compressed LZFSE block decoder state
/// </summary>
internal struct LzfseCompressedBlockDecoderState
{
    public uint NMatches;
    public uint NLmdPayloadBytes;
    public int CurrentLiteralPos;
    public int LValue, MValue, DValue;
    public FseInStream LmdInStream;
    public int LmdInBuf;
    public ushort LState, MState, DState;

    // FSE decoder tables
    public FseValueDecoderEntry[] LDecoder;
    public FseValueDecoderEntry[] MDecoder;
    public FseValueDecoderEntry[] DDecoder;
    public int[] LiteralDecoder;

    // Literals buffer
    public byte[] Literals;

    public LzfseCompressedBlockDecoderState()
    {
        LDecoder = new FseValueDecoderEntry[Constants.EncodeLStates];
        MDecoder = new FseValueDecoderEntry[Constants.EncodeMStates];
        DDecoder = new FseValueDecoderEntry[Constants.EncodeDStates];
        LiteralDecoder = new int[Constants.EncodeLiteralStates];
        Literals = new byte[Constants.LiteralsPerBlock + 64];
    }
}

/// <summary>
/// Uncompressed block decoder state
/// </summary>
internal struct UncompressedBlockDecoderState
{
    public uint NRawBytes;
}

/// <summary>
/// LZVN compressed block decoder state
/// </summary>
internal struct LzvnCompressedBlockDecoderState
{
    public uint NRawBytes;
    public uint NPayloadBytes;
    public uint DPrev;
}

/// <summary>
/// Main LZFSE decoder state
/// </summary>
internal ref struct LzfseDecoderState
{
    // Source buffer pointers
    public int Src;
    public int SrcBegin;
    public int SrcEnd;

    // Destination buffer pointers
    public int Dst;
    public int DstBegin;
    public int DstEnd;

    // Stream state
    public bool EndOfStream;
    public uint BlockMagic;

    // Block-specific decoder states
    public LzfseCompressedBlockDecoderState CompressedLzfseBlockState;
    public LzvnCompressedBlockDecoderState CompressedLzvnBlockState;
    public UncompressedBlockDecoderState UncompressedBlockState;

    // Buffers
    public ReadOnlySpan<byte> SrcBuffer;
    public Span<byte> DstBuffer;

}
