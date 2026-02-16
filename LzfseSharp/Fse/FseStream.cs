using System.Runtime.CompilerServices;

namespace LzfseSharp.Fse;

/// <summary>
/// FSE input stream for reading bits backwards from a buffer
/// </summary>
internal struct FseInStream
{
    /// <summary>
    /// Input bits accumulator
    /// </summary>
    public ulong Accum;

    /// <summary>
    /// Number of valid bits in accumulator
    /// </summary>
    public int AccumNbits;

    /// <summary>
    /// Initialize the FSE input stream
    /// </summary>
    /// <param name="n">Initial bit count</param>
    /// <param name="bufferPtr">Pointer to buffer position (updated to point to new position)</param>
    /// <param name="bufferStart">Start of buffer</param>
    /// <param name="buffer">Full buffer</param>
    /// <returns>0 if OK, -1 on error</returns>
    public int Init(int n, ref int bufferPtr, int bufferStart, ReadOnlySpan<byte> buffer)
    {
        if (n != 0)
        {
            if (bufferPtr < bufferStart + 8)
                return -1; // out of range

            bufferPtr -= 8;
            Accum = Core.MemoryOperations.Load8(buffer[bufferPtr..]);
            AccumNbits = n + 64;
        }
        else
        {
            if (bufferPtr < bufferStart + 7)
                return -1; // out of range

            bufferPtr -= 7;
            Accum = Core.MemoryOperations.Load8(buffer[bufferPtr..]) & 0xffffffffffffff;
            AccumNbits = n + 56;
        }

        if (AccumNbits < 56 || AccumNbits >= 64 || (Accum >> AccumNbits) != 0)
            return -1; // invalid input

        return 0; // OK
    }

    /// <summary>
    /// Flush the FSE input stream (read more bytes from buffer)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Flush(ref int bufferPtr, int bufferStart, ReadOnlySpan<byte> buffer)
    {
        // Get number of bits to add to bring us into the desired range [56, 63]
        int nbits = (63 - AccumNbits) & -8;
        // Convert bits to bytes and decrement buffer address
        int newBufferPtr = bufferPtr - (nbits >> 3);
        if (newBufferPtr < bufferStart)
            return -1; // out of range

        bufferPtr = newBufferPtr;
        ulong incoming = Core.MemoryOperations.Load8(buffer[newBufferPtr..]);
        // Update the accumulator
        Accum = (Accum << nbits) | Core.BitOperations.ExtractBits(incoming, 0, nbits);
        AccumNbits += nbits;

        return 0; // OK
    }

    /// <summary>
    /// Pull n bits from the stream
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Pull(int n)
    {
        AccumNbits -= n;
        ulong result = Accum >> AccumNbits;
        Accum = Core.BitOperations.ExtractBits(Accum, 0, AccumNbits);
        return result;
    }
}
