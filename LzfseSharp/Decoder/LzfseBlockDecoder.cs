using LzfseSharp.Core;
using LzfseSharp.Fse;

namespace LzfseSharp.Decoder;

/// <summary>
/// LZFSE compressed block decoder
/// </summary>
internal static class LzfseBlockDecoder
{
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
    public static int DecodeLmd(ref LzfseDecoderState state)
    {
        ref LzfseCompressedBlockDecoderState blockState = ref state.CompressedLzfseBlockState;
        ushort lState = blockState.LState;
        ushort mState = blockState.MState;
        ushort dState = blockState.DState;
        FseInStream inStream = blockState.LmdInStream;
        int sourceStart = state.SourceStart;
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
            if (!ExecuteMatch(state, ref blockState, ref destinationPosition, ref literalPosition, ref literalLength, ref matchLength, ref matchDistance, ref remainingBytes))
            {
                return SaveStateAndReturn(ref state, ref blockState, lState, mState, dState, inStream, sourcePosition, literalPosition, destinationPosition, symbols, literalLength, matchLength, matchDistance);
            }
        }

        while (symbols > 0)
        {
            // Decode next L, M, D triplet
            var flushResult = inStream.Flush(sourcePosition, sourceStart, state.SourceBuffer);
            if (flushResult.Status != 0)
                return Constants.StatusError;
            sourcePosition = flushResult.BufferPtr;

            literalLength = FseDecoder.ValueDecode(ref lState, blockState.LDecoder, ref inStream);

            if (literalPosition + literalLength >= blockState.Literals.Length)
                return Constants.StatusError;

            flushResult = inStream.Flush(sourcePosition, sourceStart, state.SourceBuffer);
            if (flushResult.Status != 0)
                return Constants.StatusError;
            sourcePosition = flushResult.BufferPtr;

            matchLength = FseDecoder.ValueDecode(ref mState, blockState.MDecoder, ref inStream);

            flushResult = inStream.Flush(sourcePosition, sourceStart, state.SourceBuffer);
            if (flushResult.Status != 0)
                return Constants.StatusError;
            sourcePosition = flushResult.BufferPtr;

            int newDistance = FseDecoder.ValueDecode(ref dState, blockState.DDecoder, ref inStream);
            matchDistance = newDistance != 0 ? newDistance : matchDistance;
            symbols--;

            if (!ExecuteMatch(state, ref blockState, ref destinationPosition, ref literalPosition, ref literalLength, ref matchLength, ref matchDistance, ref remainingBytes))
            {
                return SaveStateAndReturn(ref state, ref blockState, lState, mState, dState, inStream, sourcePosition, literalPosition, destinationPosition, symbols, literalLength, matchLength, matchDistance);
            }
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

    private static bool ExecuteMatch(
        LzfseDecoderState state,
        ref LzfseCompressedBlockDecoderState blockState,
        ref int destinationPosition,
        ref int literalPosition,
        ref int literalLength,
        ref int matchLength,
        ref int matchDistance,
        ref long remainingBytes)
    {
        // Validate match distance
        if ((uint)matchDistance > destinationPosition + literalLength - state.DestinationStart)
            return false; // Invalid distance

        const int SafetyMargin = 32;
        const int MinDistanceForFastCopy = 8;

        if (literalLength + matchLength <= remainingBytes)
        {
            // Fast path: plenty of space remaining
            remainingBytes -= literalLength + matchLength;

            // Copy literal
            Copy(state.DestinationBuffer[destinationPosition..], blockState.Literals.AsSpan()[literalPosition..], literalLength);
            destinationPosition += literalLength;
            literalPosition += literalLength;

            // Copy match
            if (matchDistance >= MinDistanceForFastCopy || matchDistance >= matchLength)
            {
                Copy(state.DestinationBuffer[destinationPosition..], state.DestinationBuffer[(destinationPosition - matchDistance)..], matchLength);
            }
            else
            {
                // Overlapping copy for small distances
                for (int i = 0; i < matchLength; i++)
                    state.DestinationBuffer[destinationPosition + i] = state.DestinationBuffer[destinationPosition + i - matchDistance];
            }
            destinationPosition += matchLength;
            return true;
        }

        // Slow path: near end of buffer
        remainingBytes += SafetyMargin;

        // Copy literal
        if (literalLength <= remainingBytes)
        {
            for (int i = 0; i < literalLength; i++)
                state.DestinationBuffer[destinationPosition + i] = blockState.Literals[literalPosition + i];
            destinationPosition += literalLength;
            literalPosition += literalLength;
            remainingBytes -= literalLength;
            literalLength = 0;
        }
        else
        {
            int bytesToCopy = (int)remainingBytes;
            for (int i = 0; i < bytesToCopy; i++)
                state.DestinationBuffer[destinationPosition + i] = blockState.Literals[literalPosition + i];
            destinationPosition += bytesToCopy;
            literalPosition += bytesToCopy;
            literalLength -= bytesToCopy;
            return false; // Destination buffer is full
        }

        // Copy match
        if (matchLength <= remainingBytes)
        {
            for (int i = 0; i < matchLength; i++)
                state.DestinationBuffer[destinationPosition + i] = state.DestinationBuffer[destinationPosition + i - matchDistance];
            destinationPosition += matchLength;
            remainingBytes -= matchLength;
            matchLength = 0;
        }
        else
        {
            int bytesToCopy = (int)remainingBytes;
            for (int i = 0; i < bytesToCopy; i++)
                state.DestinationBuffer[destinationPosition + i] = state.DestinationBuffer[destinationPosition + i - matchDistance];
            destinationPosition += bytesToCopy;
            matchLength -= bytesToCopy;
            return false; // Destination buffer is full
        }

        remainingBytes -= SafetyMargin;
        return true;
    }
}
