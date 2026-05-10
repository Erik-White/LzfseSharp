using System.Buffers;
using LzfseSharp.Fse;

namespace LzfseSharp.Decoder;

/// <summary>
/// Compressed LZFSE block decoder state. Large scratch arrays (~46 KB total) are
/// rented from <see cref="ArrayPool{T}.Shared"/> in <see cref="Rent"/> and returned
/// in <see cref="Return"/>; callers must pair the two to avoid leaking rented arrays.
/// </summary>
internal struct LzfseCompressedBlockDecoderState
{
    public const int LiteralsBufferLength = Constants.LiteralsPerBlock + 64;

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

    // Literals buffer. Pooled arrays may be larger than LiteralsBufferLength; callers
    // that care about the logical size must use the constant instead of .Length.
    public byte[] Literals;

    public static LzfseCompressedBlockDecoderState Rent()
    {
        return new LzfseCompressedBlockDecoderState
        {
            LDecoder = ArrayPool<FseValueDecoderEntry>.Shared.Rent(Constants.EncodeLStates),
            MDecoder = ArrayPool<FseValueDecoderEntry>.Shared.Rent(Constants.EncodeMStates),
            DDecoder = ArrayPool<FseValueDecoderEntry>.Shared.Rent(Constants.EncodeDStates),
            LiteralDecoder = ArrayPool<int>.Shared.Rent(Constants.EncodeLiteralStates),
            Literals = ArrayPool<byte>.Shared.Rent(LiteralsBufferLength),
        };
    }

    public void Return()
    {
        if (LDecoder is not null) ArrayPool<FseValueDecoderEntry>.Shared.Return(LDecoder);
        if (MDecoder is not null) ArrayPool<FseValueDecoderEntry>.Shared.Return(MDecoder);
        if (DDecoder is not null) ArrayPool<FseValueDecoderEntry>.Shared.Return(DDecoder);
        if (LiteralDecoder is not null) ArrayPool<int>.Shared.Return(LiteralDecoder);
        if (Literals is not null) ArrayPool<byte>.Shared.Return(Literals);
        LDecoder = null!;
        MDecoder = null!;
        DDecoder = null!;
        LiteralDecoder = null!;
        Literals = null!;
    }
}
