using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LzfseSharp.Core;

/// <summary>
/// Memory load/store operations for unaligned access
/// </summary>
internal static class MemoryOperations
{
    /// <summary>
    /// Load 2 bytes from memory (unaligned)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Load2(ReadOnlySpan<byte> ptr)
    {
        return MemoryMarshal.Read<ushort>(ptr);
    }

    /// <summary>
    /// Load 4 bytes from memory (unaligned)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Load4(ReadOnlySpan<byte> ptr)
    {
        return MemoryMarshal.Read<uint>(ptr);
    }

    /// <summary>
    /// Load 8 bytes from memory (unaligned)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Load8(ReadOnlySpan<byte> ptr)
    {
        return MemoryMarshal.Read<ulong>(ptr);
    }

    /// <summary>
    /// Store 2 bytes to memory (unaligned)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store2(Span<byte> ptr, ushort data)
    {
        MemoryMarshal.Write(ptr, in data);
    }

    /// <summary>
    /// Store 4 bytes to memory (unaligned)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store4(Span<byte> ptr, uint data)
    {
        MemoryMarshal.Write(ptr, in data);
    }

    /// <summary>
    /// Store 8 bytes to memory (unaligned)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Store8(Span<byte> ptr, ulong data)
    {
        MemoryMarshal.Write(ptr, in data);
    }

    /// <summary>
    /// Copy 8 bytes from source to destination
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Copy8(Span<byte> dst, ReadOnlySpan<byte> src)
    {
        Store8(dst, Load8(src));
    }

    /// <summary>
    /// Copy 16 bytes from source to destination
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Copy16(Span<byte> dst, ReadOnlySpan<byte> src)
    {
        ulong m0 = Load8(src);
        ulong m1 = Load8(src[8..]);
        Store8(dst, m0);
        Store8(dst[8..], m1);
    }
}
