using System.Runtime.CompilerServices;

namespace LzfseSharp.Fse;

/// <summary>
/// FSE input stream for reading bits backwards from a buffer
/// </summary>
internal struct FseInStream
{
    internal readonly record struct InitResult(int BufferPtr, int Status);
    internal readonly record struct FlushResult(int BufferPtr, int Status);

    /// <summary>
    /// Input bits accumulator
    /// </summary>
    public ulong Accumulator;

    /// <summary>
    /// Number of valid bits in accumulator
    /// </summary>
    public int AccumulatorBitCount;

    /// <summary>
    /// Initialize the FSE input stream
    /// </summary>
    /// <param name="initialBitCount">Initial bit count</param>
    /// <param name="bufferPtr">Pointer to buffer position</param>
    /// <param name="bufferStart">Start of buffer</param>
    /// <param name="buffer">Full buffer</param>
    /// <returns>Result containing updated buffer pointer and status (0 if OK, -1 on error)</returns>
    public InitResult Init(int initialBitCount, int bufferPtr, int bufferStart, ReadOnlySpan<byte> buffer)
    {
        if (initialBitCount != 0)
        {
            if (bufferPtr < bufferStart + 8)
                return new InitResult(bufferPtr, -1); // out of range

            bufferPtr -= 8;
            Accumulator = Core.MemoryOperations.Load8(buffer[bufferPtr..]);
            AccumulatorBitCount = initialBitCount + 64;
        }
        else
        {
            if (bufferPtr < bufferStart + 7)
                return new InitResult(bufferPtr, -1); // out of range

            bufferPtr -= 7;
            Accumulator = Core.MemoryOperations.Load8(buffer[bufferPtr..]) & 0xffffffffffffff;
            AccumulatorBitCount = initialBitCount + 56;
        }

        if (AccumulatorBitCount < 56 || AccumulatorBitCount >= 64 || (Accumulator >> AccumulatorBitCount) != 0)
            return new InitResult(bufferPtr, -1); // invalid input

        return new InitResult(bufferPtr, 0); // OK
    }

    /// <summary>
    /// Flush the FSE input stream (read more bytes from buffer)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FlushResult Flush(int bufferPtr, int bufferStart, ReadOnlySpan<byte> buffer)
    {
        // Get number of bits to add to bring us into the desired range [56, 63]
        int bitsToAdd = (63 - AccumulatorBitCount) & -8;
        // Convert bits to bytes and decrement buffer address
        int newBufferPtr = bufferPtr - (bitsToAdd >> 3);
        if (newBufferPtr < bufferStart)
            return new FlushResult(bufferPtr, -1); // out of range

        ulong incoming = Core.MemoryOperations.Load8(buffer[newBufferPtr..]);
        // Update the accumulator
        Accumulator = (Accumulator << bitsToAdd) | Core.BitOperations.ExtractBits(incoming, 0, bitsToAdd);
        AccumulatorBitCount += bitsToAdd;

        return new FlushResult(newBufferPtr, 0); // OK
    }

    /// <summary>
    /// Pull bits from the stream
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Pull(int bitCount)
    {
        AccumulatorBitCount -= bitCount;
        ulong result = Accumulator >> AccumulatorBitCount;
        Accumulator = Core.BitOperations.ExtractBits(Accumulator, 0, AccumulatorBitCount);

        return result;
    }
}
