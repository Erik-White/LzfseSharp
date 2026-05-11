namespace LzfseSharp;

/// <summary>
/// LZFSE internal constants and definitions
/// </summary>
internal static class Constants
{
    // Block magic numbers
    public const uint NoBlockMagic = 0x00000000;              // 0    (invalid)
    public const uint EndOfStreamBlockMagic = 0x24787662;     // bvx$ (end of stream)
    public const uint UncompressedBlockMagic = 0x2d787662;    // bvx- (raw data)
    public const uint CompressedV1BlockMagic = 0x31787662;    // bvx1 (lzfse compressed, uncompressed tables)
    public const uint CompressedV2BlockMagic = 0x32787662;    // bvx2 (lzfse compressed, compressed tables)
    public const uint CompressedLzvnBlockMagic = 0x6e787662;  // bvxn (lzvn compressed)

    // LZFSE internal status codes
    public const int StatusOk = 0;
    public const int StatusSrcEmpty = -1;
    public const int StatusDstFull = -2;
    public const int StatusError = -3;

    // Encoding symbols and states
    public const int EncodeLSymbols = 20;
    public const int EncodeMSymbols = 20;
    public const int EncodeDSymbols = 64;
    public const int EncodeLiteralSymbols = 256;
    public const int EncodeLStates = 64;
    public const int EncodeMStates = 64;
    public const int EncodeDStates = 256;
    public const int EncodeLiteralStates = 1024;

    public const int MatchesPerBlock = 10000;
    public const int LiteralsPerBlock = 4 * MatchesPerBlock;

    // Block header sizes
    public const int UncompressedBlockHeaderSize = 8;
    public const int LzvnCompressedBlockHeaderSize = 12;
    public const int V2FixedHeaderSize = 32;

    // V1 header layout:
    //   8 * uint32  (magic, n_raw_bytes, n_payload_bytes, n_literals,
    //                n_matches, n_literal_payload_bytes, n_lmd_payload_bytes, literal_bits)
    //   4 * uint16  (literal_state[4])
    //   1 * uint32  (lmd_bits)
    //   3 * uint16  (l_state, m_state, d_state)
    //   freq tables (uint16 per symbol)
    public const int V1HeaderSize = 8 * sizeof(uint) + 4 * sizeof(ushort)
                                  + sizeof(uint) + 3 * sizeof(ushort)
                                  + sizeof(ushort) * (EncodeLSymbols + EncodeMSymbols +
                                                      EncodeDSymbols + EncodeLiteralSymbols);

    // Maximum encodable values
    public const int EncodeMaxLValue = 315;
    public const int EncodeMaxMValue = 2359;
    public const int EncodeMaxDValue = 262139;

    // L, M, D extra bits and base values
    private static readonly byte[] _lExtraBits =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 5, 8
    ];

    private static readonly int[] _lBaseValue =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 20, 28, 60
    ];

    private static readonly byte[] _mExtraBits =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 5, 8, 11
    ];

    private static readonly int[] _mBaseValue =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 24, 56, 312
    ];

    private static readonly byte[] _dExtraBits =
    [
        0,  0,  0,  0,  1,  1,  1,  1,  2,  2,  2,  2,  3,  3,  3,  3,
        4,  4,  4,  4,  5,  5,  5,  5,  6,  6,  6,  6,  7,  7,  7,  7,
        8,  8,  8,  8,  9,  9,  9,  9,  10, 10, 10, 10, 11, 11, 11, 11,
        12, 12, 12, 12, 13, 13, 13, 13, 14, 14, 14, 14, 15, 15, 15, 15
    ];

    private static readonly int[] _dBaseValue =
    [
        0,      1,      2,      3,     4,     6,     8,     10,    12,    16,
        20,     24,     28,     36,    44,    52,    60,    76,    92,    108,
        124,    156,    188,    220,   252,   316,   380,   444,   508,   636,
        764,    892,    1020,   1276,  1532,  1788,  2044,  2556,  3068,  3580,
        4092,   5116,   6140,   7164,  8188,  10236, 12284, 14332, 16380, 20476,
        24572,  28668,  32764,  40956, 49148, 57340, 65532, 81916, 98300, 114684,
        131068, 163836, 196604, 229372
    ];

    public static ReadOnlySpan<byte> LExtraBits => _lExtraBits;
    public static ReadOnlySpan<int> LBaseValue => _lBaseValue;
    public static ReadOnlySpan<byte> MExtraBits => _mExtraBits;
    public static ReadOnlySpan<int> MBaseValue => _mBaseValue;
    public static ReadOnlySpan<byte> DExtraBits => _dExtraBits;
    public static ReadOnlySpan<int> DBaseValue => _dBaseValue;
}
