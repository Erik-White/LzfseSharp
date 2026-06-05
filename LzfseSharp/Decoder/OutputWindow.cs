using System.Buffers;

namespace LzfseSharp.Decoder;

/// <summary>
/// A bounded, reusable buffer that the decoder writes decompressed bytes into and
/// periodically flushes to the destination stream. LZFSE match copies reference
/// backwards into already-emitted output by up to <see cref="Constants.EncodeMaxDValue"/>
/// bytes, so the window always keeps at least that many trailing bytes resident; only the
/// bytes older than that are flushed and dropped. This bounds memory to a fixed size
/// regardless of the decompressed length.
/// </summary>
internal sealed class OutputWindow
{
    // Bytes that must remain resident behind the write position so the largest possible
    // back-reference can still be satisfied. LZFSE's max distance (262 139) dominates
    // LZVN's (65 535); round up for headroom.
    public const int RetainBytes = 1 << 18; // 262 144 >= EncodeMaxDValue

    // Total buffer capacity. The space past RetainBytes is the room a decode pass may
    // produce before the next flush is forced.
    private const int Capacity = RetainBytes + (1 << 20); // + 1 MiB of decode headroom

    // Bytes the decoders may write past their logical end position. LzfseBlockDecoder
    // keeps a 32-byte safety margin and the LZVN copy fast paths use 8-byte stores that
    // can overshoot the last logical byte. WritableEnd is held this far below Capacity so
    // that slop can never run past the array. Comfortably covers both with headroom.
    private const int DecodeSlopBytes = 64;

    private byte[] buffer;

    /// <summary>
    /// Number of valid decoded bytes currently held in buffer[0..Position). Bytes below
    /// (Position - <see cref="RetainBytes"/>) have not yet been written to the destination
    /// but are eligible to be flushed on the next <see cref="Flush"/>.
    /// </summary>
    public int Position { get; private set; }

    public OutputWindow()
    {
        buffer = ArrayPool<byte>.Shared.Rent(Capacity);
    }

    /// <summary>
    /// The writable buffer handed to the block decoders. Decoded data accumulates from
    /// <see cref="Position"/> upward; match back-references read below it.
    /// </summary>
    public Span<byte> Buffer => buffer;

    /// <summary>
    /// The exclusive upper bound the decoder may write up to before it must yield for a
    /// flush. Kept <see cref="DecodeSlopBytes"/> below the raw capacity so the decoders'
    /// fixed overshoot can never run past the array.
    /// </summary>
    public int WritableEnd => Capacity - DecodeSlopBytes;

    public void Advance(int newPosition) => Position = newPosition;

    /// <summary>
    /// Writes everything but the trailing <see cref="RetainBytes"/> to the destination and
    /// shifts the retained tail to the front, freeing room for further decoding. Returns
    /// the number of bytes shifted out so the caller can rebase window-relative positions.
    /// </summary>
    public int Flush(Stream destination)
    {
        int shift = Position - RetainBytes;
        if (shift <= 0)
            return 0;

        destination.Write(buffer, 0, shift);
        SlideRetainedTail(shift);
        return shift;
    }

    /// <summary>
    /// Asynchronous counterpart to <see cref="Flush"/>.
    /// </summary>
    public async ValueTask<int> FlushAsync(Stream destination, CancellationToken cancellationToken)
    {
        int shift = Position - RetainBytes;
        if (shift <= 0)
            return 0;

        await destination.WriteAsync(buffer.AsMemory(0, shift), cancellationToken).ConfigureAwait(false);
        SlideRetainedTail(shift);
        return shift;
    }

    // Move the trailing RetainBytes (the bytes just past the flushed prefix) to the front
    // of the buffer so decoding can continue while back-references into them stay valid.
    private void SlideRetainedTail(int shift)
    {
        System.Buffer.BlockCopy(buffer, shift, buffer, 0, RetainBytes);
        Position = RetainBytes;
    }

    /// <summary>
    /// Writes any bytes still held in the window to the destination. Called once after the
    /// end-of-stream marker is consumed.
    /// </summary>
    public void FlushRemaining(Stream destination)
    {
        if (Position > 0)
            destination.Write(buffer, 0, Position);
        Position = 0;
    }

    public async ValueTask FlushRemainingAsync(Stream destination, CancellationToken cancellationToken)
    {
        if (Position > 0)
            await destination.WriteAsync(buffer.AsMemory(0, Position), cancellationToken).ConfigureAwait(false);
        Position = 0;
    }

    public void Return()
    {
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = null!;
        }
    }
}
