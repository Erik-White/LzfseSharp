using System.Runtime.CompilerServices;

namespace LzfseSharp.Core;

/// <summary>
/// Bit manipulation utilities
/// </summary>
internal static class BitOperations
{
    /// <summary>
    /// Extracts <paramref name="length"/> bits from <paramref name="container"/>, starting at <paramref name="lsb"/>.
    /// If we view container as a bit array, we extract container[lsb:lsb+length].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ExtractBits(ulong container, int lsb, int length)
    {
        if (length >= 64)
            return container >> lsb;

        ulong mask = (1UL << length) - 1;
        return (container >> lsb) & mask;
    }

    /// <summary>
    /// Extracts <paramref name="length"/> bits from <paramref name="container"/>, starting at <paramref name="lsb"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ExtractBits(uint container, int lsb, int length)
    {
        if (length >= 32)
            return container >> lsb;

        uint mask = (1U << length) - 1;
        return (container >> lsb) & mask;
    }

    /// <summary>
    /// Extracts a field from a packed 64-bit value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetField(ulong value, int offset, int nbits)
    {
        if (nbits == 32)
            return (uint)(value >> offset);

        return (uint)((value >> offset) & ((1UL << nbits) - 1));
    }

    /// <summary>
    /// Count leading zeros
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountLeadingZeros(uint value)
    {
        return System.Numerics.BitOperations.LeadingZeroCount(value);
    }

    /// <summary>
    /// Count trailing zeros
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountTrailingZeros(uint value)
    {
        return System.Numerics.BitOperations.TrailingZeroCount(value);
    }

    /// <summary>
    /// Count trailing zeros (64-bit)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountTrailingZeros(ulong value)
    {
        return System.Numerics.BitOperations.TrailingZeroCount(value);
    }
}
