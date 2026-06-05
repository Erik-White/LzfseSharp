using LzfseSharp.Core;
using LzfseSharp.Fse;

namespace LzfseSharp.Decoder;

/// <summary>
/// LZFSE compressed block decoder
/// </summary>
internal static class LzfseBlockDecoder
{
    /// <summary>
    /// Outcome of expanding a single L/M/D triplet into the destination window.
    /// </summary>
    private enum MatchResult
    {
        /// <summary>
        /// The literal and match were fully written.
        /// </summary>
        Completed,

        /// <summary>
        /// The destination window filled before the op finished; the outstanding
        /// literal/match lengths have been written back so the op can resume after
        /// the caller makes more room.
        /// </summary>
        DestinationFull,

        /// <summary>
        /// The match distance reaches before the start of the available output. This
        /// is always a malformed stream — distinct from <see cref="DestinationFull"/>
        /// so the streaming driver does not retry it.
        /// </summary>
        InvalidDistance,
    }

    /// <summary>
    /// Copy data with proper handling of overlapping match copies
    /// </summary>
    private static void Copy(Span<byte> destination, ReadOnlySpan<byte> source, int length)
    {
        const int BytesPerCopy = 8;
        int destinationIndex = 0;
        int sourceIndex = 0;
        int alignedEnd = (length / BytesPerCopy) * BytesPerCopy;

        // Copy 8 bytes at a time for better performance
        while (destinationIndex < alignedEnd)
        {
            MemoryOperations.Copy8(destination[destinationIndex..], source[sourceIndex..]);
            destinationIndex += BytesPerCopy;
            sourceIndex += BytesPerCopy;
        }

        // Copy remaining bytes
        while (destinationIndex < length)
        {
            destination[destinationIndex] = source[sourceIndex];
            destinationIndex++;
            sourceIndex++;
        }
    }

    /// <summary>
    /// Decode L, M, D triplets and expand matches
    /// </summary>
    public static int DecodeLmd(ref LzfseDecoderState state, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ref LzfseCompressedBlockDecoderState blockState = ref state.CompressedLzfseBlockState;
        ushort lState = blockState.LState;
        ushort mState = blockState.MState;
        ushort dState = blockState.DState;
        FseInStream inStream = blockState.LmdInStream;
        const int sourceStart = 0;
        int sourcePosition = state.SourcePosition + blockState.LmdInBuf;
        int literalPosition = blockState.CurrentLiteralPos;
        int destinationPosition = state.DestinationPosition;
        uint symbols = blockState.NMatches;
        int literalLength = blockState.LValue;
        int matchLength = blockState.MValue;
        int matchDistance = blockState.DValue;

        const int SafetyMargin = 32;
        long remainingBytes = state.DestinationEnd - destinationPosition - SafetyMargin;

        // If literalLength or matchLength is non-zero, we have a pending match to complete
        if (literalLength != 0 || matchLength != 0)
        {
            MatchResult resume = ExecuteMatch(destination, ref blockState, ref destinationPosition, ref literalPosition, ref literalLength, ref matchLength, ref matchDistance, ref remainingBytes);
            if (resume == MatchResult.InvalidDistance)
                return Constants.StatusError;
            if (resume == MatchResult.DestinationFull)
                return SaveStateAndReturn(ref state, ref blockState, lState, mState, dState, inStream, sourcePosition, literalPosition, destinationPosition, symbols, literalLength, matchLength, matchDistance);
        }

        while (symbols > 0)
        {
            // Decode next L, M, D triplet
            var flushResult = inStream.Flush(sourcePosition, sourceStart, source);
            if (flushResult.Status != 0)
                return Constants.StatusError;
            sourcePosition = flushResult.BufferPtr;

            literalLength = FseDecoder.ValueDecode(ref lState, blockState.LDecoder, ref inStream);

            if (literalPosition + literalLength >= LzfseCompressedBlockDecoderState.LiteralsBufferLength)
                return Constants.StatusError;

            flushResult = inStream.Flush(sourcePosition, sourceStart, source);
            if (flushResult.Status != 0)
                return Constants.StatusError;
            sourcePosition = flushResult.BufferPtr;

            matchLength = FseDecoder.ValueDecode(ref mState, blockState.MDecoder, ref inStream);

            flushResult = inStream.Flush(sourcePosition, sourceStart, source);
            if (flushResult.Status != 0)
                return Constants.StatusError;
            sourcePosition = flushResult.BufferPtr;

            int newDistance = FseDecoder.ValueDecode(ref dState, blockState.DDecoder, ref inStream);
            matchDistance = newDistance != 0 ? newDistance : matchDistance;
            symbols--;

            MatchResult result = ExecuteMatch(destination, ref blockState, ref destinationPosition, ref literalPosition, ref literalLength, ref matchLength, ref matchDistance, ref remainingBytes);
            if (result == MatchResult.InvalidDistance)
                return Constants.StatusError;
            if (result == MatchResult.DestinationFull)
                return SaveStateAndReturn(ref state, ref blockState, lState, mState, dState, inStream, sourcePosition, literalPosition, destinationPosition, symbols, literalLength, matchLength, matchDistance);
        }

        // Block complete
        state.DestinationPosition = destinationPosition;
        return Constants.StatusOk;
    }

    private static int SaveStateAndReturn(
        ref LzfseDecoderState state,
        ref LzfseCompressedBlockDecoderState blockState,
        ushort lState,
        ushort mState,
        ushort dState,
        FseInStream inStream,
        int sourcePosition,
        int literalPosition,
        int destinationPosition,
        uint symbols,
        int literalLength,
        int matchLength,
        int matchDistance)
    {
        blockState.LValue = literalLength;
        blockState.MValue = matchLength;
        blockState.DValue = matchDistance;
        blockState.LState = lState;
        blockState.MState = mState;
        blockState.DState = dState;
        blockState.LmdInStream = inStream;
        blockState.NMatches = symbols;
        blockState.LmdInBuf = sourcePosition - state.SourcePosition;
        blockState.CurrentLiteralPos = literalPosition;
        state.DestinationPosition = destinationPosition;
        return Constants.StatusDstFull;
    }

    private static MatchResult ExecuteMatch(
        Span<byte> destination,
        ref LzfseCompressedBlockDecoderState blockState,
        ref int destinationPosition,
        ref int literalPosition,
        ref int literalLength,
        ref int matchLength,
        ref int matchDistance,
        ref long remainingBytes)
    {
        // Validate match distance: cannot reach before the start of the available output.
        // destinationPosition is window-relative; the streaming window always retains at
        // least the maximum encodable distance behind it, so a valid back-reference stays
        // in range and this only rejects genuinely malformed distances.
        if ((uint)matchDistance > destinationPosition + literalLength)
            return MatchResult.InvalidDistance;

        const int SafetyMargin = 32;
        const int MinDistanceForFastCopy = 8;

        if (literalLength + matchLength <= remainingBytes)
        {
            // Fast path: plenty of space remaining
            remainingBytes -= literalLength + matchLength;

            // Copy literal
            Copy(destination[destinationPosition..], blockState.Literals.AsSpan()[literalPosition..], literalLength);
            destinationPosition += literalLength;
            literalPosition += literalLength;

            // Copy match
            if (matchDistance >= MinDistanceForFastCopy || matchDistance >= matchLength)
            {
                Copy(destination[destinationPosition..], destination[(destinationPosition - matchDistance)..], matchLength);
            }
            else
            {
                // Overlapping copy for small distances
                for (int i = 0; i < matchLength; i++)
                    destination[destinationPosition + i] = destination[destinationPosition + i - matchDistance];
            }
            destinationPosition += matchLength;
            return MatchResult.Completed;
        }

        // Slow path: near end of buffer
        remainingBytes += SafetyMargin;

        // Copy literal
        if (literalLength <= remainingBytes)
        {
            for (int i = 0; i < literalLength; i++)
                destination[destinationPosition + i] = blockState.Literals[literalPosition + i];
            destinationPosition += literalLength;
            literalPosition += literalLength;
            remainingBytes -= literalLength;
            literalLength = 0;
        }
        else
        {
            int bytesToCopy = (int)remainingBytes;
            for (int i = 0; i < bytesToCopy; i++)
                destination[destinationPosition + i] = blockState.Literals[literalPosition + i];
            destinationPosition += bytesToCopy;
            literalPosition += bytesToCopy;
            literalLength -= bytesToCopy;
            return MatchResult.DestinationFull;
        }

        // Copy match
        if (matchLength <= remainingBytes)
        {
            for (int i = 0; i < matchLength; i++)
                destination[destinationPosition + i] = destination[destinationPosition + i - matchDistance];
            destinationPosition += matchLength;
            remainingBytes -= matchLength;
            matchLength = 0;
        }
        else
        {
            int bytesToCopy = (int)remainingBytes;
            for (int i = 0; i < bytesToCopy; i++)
                destination[destinationPosition + i] = destination[destinationPosition + i - matchDistance];
            destinationPosition += bytesToCopy;
            matchLength -= bytesToCopy;
            return MatchResult.DestinationFull;
        }

        remainingBytes -= SafetyMargin;
        return MatchResult.Completed;
    }
}
