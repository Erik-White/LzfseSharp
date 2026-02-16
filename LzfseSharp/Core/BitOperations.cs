using System.Runtime.CompilerServices;

namespace LzfseSharp.Core;

/// <summary>
/// Bit manipulation utilities
/// </summary>
internal static class BitOperations
{
    /// <summary>
    /// Extracts <paramref name="length"/> bits from <paramref name="container"/>, starting at <paramref name="leastSignificantBit"/>.
    /// If we view container as a bit array, we extract container[lsb:lsb+length].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ExtractBits(ulong container, int leastSignificantBit, int length)
    {
        if (length >= 64)
            return container >> leastSignificantBit;

        ulong mask = (1UL << length) - 1;

        return (container >> leastSignificantBit) & mask;
    }

    /// <summary>
    /// Extracts <paramref name="length"/> bits from <paramref name="container"/>, starting at <paramref name="leastSignificantBit"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ExtractBits(uint container, int leastSignificantBit, int length)
    {
        if (length >= 32)
            return container >> leastSignificantBit;

        uint mask = (1U << length) - 1;

        return (container >> leastSignificantBit) & mask;
    }

    /// <summary>
    /// Extracts a field from a packed 64-bit value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetField(ulong value, int offset, int bitCount)
        => bitCount == 32
            ? (uint)(value >> offset)
            : (uint)((value >> offset) & ((1UL << bitCount) - 1));
}
