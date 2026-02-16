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
