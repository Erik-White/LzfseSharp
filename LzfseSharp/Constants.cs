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

    // Maximum encodable values
    public const int EncodeMaxLValue = 315;
    public const int EncodeMaxMValue = 2359;
    public const int EncodeMaxDValue = 262139;

    // L, M, D extra bits and base values
    public static readonly byte[] LExtraBits =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 5, 8
    ];

    public static readonly int[] LBaseValue =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 20, 28, 60
    ];

    public static readonly byte[] MExtraBits =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 5, 8, 11
    ];

    public static readonly int[] MBaseValue =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 24, 56, 312
    ];

    public static readonly byte[] DExtraBits =
    [
        0,  0,  0,  0,  1,  1,  1,  1,  2,  2,  2,  2,  3,  3,  3,  3,
        4,  4,  4,  4,  5,  5,  5,  5,  6,  6,  6,  6,  7,  7,  7,  7,
        8,  8,  8,  8,  9,  9,  9,  9,  10, 10, 10, 10, 11, 11, 11, 11,
        12, 12, 12, 12, 13, 13, 13, 13, 14, 14, 14, 14, 15, 15, 15, 15
    ];

    public static readonly int[] DBaseValue =
    [
        0,      1,      2,      3,     4,     6,     8,     10,    12,    16,
        20,     24,     28,     36,    44,    52,    60,    76,    92,    108,
        124,    156,    188,    220,   252,   316,   380,   444,   508,   636,
        764,    892,    1020,   1276,  1532,  1788,  2044,  2556,  3068,  3580,
        4092,   5116,   6140,   7164,  8188,  10236, 12284, 14332, 16380, 20476,
        24572,  28668,  32764,  40956, 49148, 57340, 65532, 81916, 98300, 114684,
        131068, 163836, 196604, 229372
    ];
}
