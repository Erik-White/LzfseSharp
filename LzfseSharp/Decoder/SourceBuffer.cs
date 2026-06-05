using System.Buffers;

namespace LzfseSharp.Decoder;

/// <summary>
/// A reusable, growable buffer that feeds compressed bytes from the source stream to the
/// decoder. The decoder needs an entire LZFSE/LZVN block resident at once (the FSE bit
/// reader scans a block's payload backwards from its end), so this buffer compacts the
/// already-consumed prefix and reads more — growing only when a single block is larger
/// than the current capacity.
/// </summary>
internal sealed class SourceBuffer
{
    private const int InitialCapacity = 1 << 16; // 64 KiB; most blocks fit without growth

    private byte[] buffer;

    /// <summary>
    /// The decoder's read offset. The driver writes the decoder's consumed position here
    /// before requesting more data; <see cref="Fill"/> drops everything before it.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Number of valid bytes held in the buffer.
    /// </summary>
    public int Available { get; private set; }

    public SourceBuffer()
    {
        buffer = ArrayPool<byte>.Shared.Rent(InitialCapacity);
    }

    public ReadOnlySpan<byte> Data => buffer.AsSpan(0, Available);

    /// <summary>
    /// Drops the consumed prefix, makes room (growing if a single block fills the buffer),
    /// and reads more bytes from <paramref name="source"/>. Returns false when the stream
    /// is at its end and no new bytes were read — i.e. the decoder wanted more input but
    /// none remains.
    /// </summary>
    public bool Fill(Stream source)
    {
        Compact();
        EnsureWritableSpace();

        int read = source.Read(buffer, Available, buffer.Length - Available);
        Available += read;
        return read > 0;
    }

    /// <summary>
    /// Asynchronous counterpart to <see cref="Fill"/>.
    /// </summary>
    public async ValueTask<bool> FillAsync(Stream source, CancellationToken cancellationToken)
    {
        Compact();
        EnsureWritableSpace();

        int read = await source.ReadAsync(buffer.AsMemory(Available, buffer.Length - Available), cancellationToken).ConfigureAwait(false);
        Available += read;
        return read > 0;
    }

    private void Compact()
    {
        if (Position == 0)
            return;

        int remaining = Available - Position;
        if (remaining > 0)
            System.Buffer.BlockCopy(buffer, Position, buffer, 0, remaining);
        Available = remaining;
        Position = 0;
    }

    private void EnsureWritableSpace()
    {
        if (Available < buffer.Length)
            return;

        // The buffer is entirely unconsumed valid data and the decoder still needs more:
        // the current block is larger than capacity. Double it. The decoder rejects any
        // block declaring more than Constants.MaxBlockPayloadBytes before requiring it
        // resident, so a well-behaved caller never grows this past ~2 * that bound; the
        // checked multiply is a backstop so a future caller can't silently overflow it.
        int newCapacity = checked(buffer.Length * 2);
        byte[] larger = ArrayPool<byte>.Shared.Rent(newCapacity);
        System.Buffer.BlockCopy(buffer, 0, larger, 0, Available);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = larger;
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
