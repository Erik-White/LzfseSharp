namespace LzfseSharp.Lzvn;

/// <summary>
/// LZVN opcode and encoding constants
/// </summary>
internal static class LzvnConstants
{
    // Opcode ranges
    public const byte LiteralOpcodeStart = 0xe0;
    public const byte LiteralOpcodeEnd = 0xf0;
    public const byte MediumDistanceOpcodeStart = 0xa0;
    public const byte MediumDistanceOpcodeEnd = 0xc0;
    public const byte SmallMatchOpcodeStart = 0xf1;

    // Specific opcodes
    public const byte LargeLiteralOpcode = 0xe0;
    public const byte LargeMatchOpcode = 0xf0;
    public const byte EndOfStreamOpcode = 6;
    public const byte NopOpcode1 = 14;
    public const byte NopOpcode2 = 22;

    // Opcode flags
    public const byte PreviousDistanceFlag = 6;
    public const byte LargeDistanceFlag = 7;

    // Encoding biases
    public const int LargeLiteralBias = 16;
    public const int LargeMatchBias = 16;
    public const int MatchLengthBias = 3;

    // Opcode lengths
    public const int EndOfStreamOpcodeLength = 8;
}
