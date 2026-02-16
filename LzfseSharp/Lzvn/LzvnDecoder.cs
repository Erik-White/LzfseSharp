using LzfseSharp.Core;
using System.Runtime.CompilerServices;

namespace LzfseSharp.Lzvn;

/// <summary>
/// LZVN low-level decoder
/// </summary>
internal static class LzvnDecoder
{
    private readonly record struct OpcodeDecodeResult(
        int Status,
        nuint LiteralLength,
        nuint MatchLength,
        nuint? MatchDistance,
        int OpcodeLength);

    /// <summary>
    /// Decode LZVN compressed data
    /// </summary>
    public static void Decode(ref LzvnDecoderState state, ReadOnlySpan<byte> srcBuffer, Span<byte> dstBuffer)
    {
        int sourceLength = state.SourceEnd - state.SourcePosition;
        int destinationLength = state.DestinationEnd - state.DestinationPosition;
        if (sourceLength == 0 || destinationLength == 0)
            return;

        int sourcePosition = state.SourcePosition;
        int destinationPosition = state.DestinationPosition;
        nuint D = (nuint)state.PreviousDistance;
        nuint M, L;
        int opcodeLength = 0;

        // Handle partially expanded match saved in state
        if (state.LiteralLength != 0 || state.MatchLength != 0)
        {
            L = state.LiteralLength;
            M = state.MatchLength;
            D = state.MatchDistance;
            opcodeLength = 0;
            state.LiteralLength = state.MatchLength = state.MatchDistance = 0;

            if (M == 0)
            {
                if (!ProcessLiteral(dstBuffer, ref destinationPosition, srcBuffer, ref sourcePosition, L, ref destinationLength, ref sourceLength, D, out var partialState1))
                {
                    state = partialState1;
                    return;
                }
            }
            else if (L == 0)
            {
                if (!ProcessMatch(dstBuffer, ref destinationPosition, M, D, ref destinationLength, srcBuffer, ref sourcePosition, 0, ref sourceLength, out var partialState2))
                {
                    state = partialState2;
                    return;
                }
            }
            else
            {
                if (!ProcessLiteralAndMatch(dstBuffer, ref destinationPosition, srcBuffer, ref sourcePosition, L, M, D, state.DestinationStart, ref destinationLength, ref sourceLength, out var partialState3))
                {
                    state = partialState3;
                    return;
                }
            }

            D = state.MatchDistance;
        }

        while (sourcePosition < state.SourceEnd)
        {
            byte opcode =srcBuffer[sourcePosition];

            // Decode opcode and extract L, M, D values
            OpcodeDecodeResult decodeResult = DecodeOpcode(opcode, srcBuffer, sourcePosition, sourceLength);
            L = decodeResult.LiteralLength;
            M = decodeResult.MatchLength;
            if (decodeResult.MatchDistance.HasValue)
                D = decodeResult.MatchDistance.Value;
            opcodeLength = decodeResult.OpcodeLength;

            switch (decodeResult.Status)
            {
                case -1:
                    // Source truncated
                    break;

                case -2:
                    // End of stream
                    state.SourcePosition = sourcePosition + opcodeLength;
                    state.EndOfStream = true;
                    state.DestinationPosition = destinationPosition;
                    state.PreviousDistance = (int)D;
                    return;

                case 0:
                    // NOP, skip and continue
                    sourcePosition += opcodeLength;
                    sourceLength -= opcodeLength;
                    continue;
            }

            // Status == 1: normal opcode
            if (decodeResult.Status != 1)
                break; // Status was -1 (source truncated)
            if (L > 0 && M > 0)
            {
                // Literal and match
                if (!ProcessLiteralAndMatch(dstBuffer, ref destinationPosition, srcBuffer, ref sourcePosition, L, M, D, state.DestinationStart, ref destinationLength, ref sourceLength, out var partialState))
                {
                    state = partialState;
                    state.MatchDistance = D;
                    return;
                }
            }
            else if (L > 0)
            {
                // Literal only
                if (!ProcessLiteral(dstBuffer, ref destinationPosition, srcBuffer, ref sourcePosition, L, ref destinationLength, ref sourceLength, D, out var partialState))
                {
                    state = partialState;
                    return;
                }
            }
            else if (M > 0)
            {
                // Match only
                if (!ProcessMatch(dstBuffer, ref destinationPosition, M, D, ref destinationLength, srcBuffer, ref sourcePosition, opcodeLength, ref sourceLength, out var partialState))
                {
                    state = partialState;
                    return;
                }
            }
            else
            {
                // Neither L nor M, just skip the opcode
                sourcePosition += opcodeLength;
                sourceLength -= opcodeLength;
            }
        }
        state.SourcePosition = sourcePosition;
        state.DestinationPosition = destinationPosition;
        state.PreviousDistance = (int)D;
    }

    private static bool ProcessLiteralAndMatch(
        Span<byte> dst,
        ref int destinationPosition,
        ReadOnlySpan<byte> src,
        ref int sourcePosition,
        nuint L,
        nuint M,
        nuint D,
        int dstStart,
        ref int destinationLength,
        ref int sourceLength,
        out LzvnDecoderState partialState)
    {
        partialState = default;

        // Advance past opcode
        int opcodeLength = sourcePosition < src.Length ? GetOpcodeLength(src[sourcePosition]) : 1;
        sourcePosition += opcodeLength;
        sourceLength -= opcodeLength;

        // Copy literal
        if (!CopyLiteralBytes(dst, ref destinationPosition, src, ref sourcePosition, L, destinationLength, ref sourceLength))
        {
            partialState.SourcePosition = sourcePosition;
            partialState.DestinationPosition = destinationPosition;
            partialState.LiteralLength = L;
            partialState.MatchLength = M;
            partialState.MatchDistance = D;
            return false;
        }

        destinationLength -= (int)L;

        // Validate match distance
        if (D > (nuint)(destinationPosition - dstStart) || D == 0)
            return false;

        // Copy match
        if (!CopyMatchBytes(dst, ref destinationPosition, M, D, destinationLength))
        {
            partialState.SourcePosition = sourcePosition;
            partialState.DestinationPosition = destinationPosition;
            partialState.LiteralLength = 0;
            partialState.MatchLength = M;
            partialState.MatchDistance = D;
            return false;
        }

        destinationLength -= (int)M;
        return true;
    }

    private static bool ProcessLiteral(
        Span<byte> dst, ref int destinationPosition,
        ReadOnlySpan<byte> src, ref int sourcePosition,
        nuint L, ref int destinationLength, ref int sourceLength,
        nuint D, out LzvnDecoderState partialState)
    {
        partialState = default;

        int opcodeLength = sourcePosition < src.Length ? GetOpcodeLength(src[sourcePosition]) : 1;
        sourcePosition += opcodeLength;
        sourceLength -= opcodeLength;

        if (!CopyLiteralBytes(dst, ref destinationPosition, src, ref sourcePosition, L, destinationLength, ref sourceLength))
        {
            partialState.SourcePosition = sourcePosition;
            partialState.DestinationPosition = destinationPosition;
            partialState.LiteralLength = L;
            partialState.MatchLength = 0;
            partialState.MatchDistance = D;
            return false;
        }

        destinationLength -= (int)L;
        return true;
    }

    private static bool ProcessMatch(
        Span<byte> dst, ref int destinationPosition,
        nuint M, nuint D, ref int destinationLength,
        ReadOnlySpan<byte> src, ref int sourcePosition,
        int opcodeLength, ref int sourceLength,
        out LzvnDecoderState partialState)
    {
        partialState = default;

        if (opcodeLength > 0)
        {
            sourcePosition += opcodeLength;
            sourceLength -= opcodeLength;
        }

        if (!CopyMatchBytes(dst, ref destinationPosition, M, D, destinationLength))
        {
            partialState.SourcePosition = sourcePosition;
            partialState.DestinationPosition = destinationPosition;
            partialState.LiteralLength = 0;
            partialState.MatchLength = M;
            partialState.MatchDistance = D;
            return false;
        }

        destinationLength -= (int)M;
        return true;
    }

    /// <summary>
    /// Calculate the opcode length based on the opcode byte
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetOpcodeLength(byte opcode)
    {
        // Check opcode category and return appropriate length
        if (opcode >= LzvnConstants.LiteralOpcodeStart && opcode < LzvnConstants.LiteralOpcodeEnd)
        {
            return opcode == LzvnConstants.LargeLiteralOpcode ? 2 : 1;
        }

        if (opcode >= LzvnConstants.MediumDistanceOpcodeStart && opcode < LzvnConstants.MediumDistanceOpcodeEnd)
        {
            return 3;
        }

        return (opcode & 7) switch
        {
            LzvnConstants.PreviousDistanceFlag => 1,
            LzvnConstants.LargeDistanceFlag => 3,
            _ => 2
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CopyLiteralBytes(Span<byte> dst, ref int destinationPosition, ReadOnlySpan<byte> src, ref int sourcePosition, nuint L, int destinationLength, ref int sourceLength)
    {
        if (destinationLength >= 4 && sourceLength >= 4 && L <= 3)
        {
            MemoryOperations.Store4(dst[destinationPosition..], MemoryOperations.Load4(src[sourcePosition..]));
        }
        else if (L <= (nuint)destinationLength && L <= (nuint)sourceLength)
        {
            for (nuint i = 0; i < L; i++)
                dst[destinationPosition + (int)i] = src[sourcePosition + (int)i];
        }
        else if (L > (nuint)destinationLength)
        {
            for (int i = 0; i < destinationLength; i++)
                dst[destinationPosition + i] = src[sourcePosition + i];
            sourcePosition += destinationLength;
            sourceLength -= destinationLength;
            return false;
        }
        else
        {
            return false;
        }

        destinationPosition += (int)L;
        sourcePosition += (int)L;
        sourceLength -= (int)L;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CopyMatchBytes(Span<byte> dst, ref int destinationPosition, nuint M, nuint D, int destinationLength)
    {
        // Fast path: 8-byte copies when distance is at least 8
        // This handles overlapping copies correctly by reading most recent writes
        if (destinationLength >= (int)(M + 7) && D >= 8)
        {
            for (nuint i = 0; i < M; i += 8)
                MemoryOperations.Store8(dst[(destinationPosition + (int)i)..], MemoryOperations.Load8(dst[(destinationPosition + (int)i - (int)D)..]));
        }
        else if (M <= (nuint)destinationLength)
        {
            for (nuint i = 0; i < M; i++)
                dst[destinationPosition + (int)i] = dst[destinationPosition + (int)i - (int)D];
        }
        else
        {
            for (int i = 0; i < destinationLength; i++)
                dst[destinationPosition + i] = dst[destinationPosition + i - (int)D];
            return false;
        }

        destinationPosition += (int)M;
        return true;
    }

    private static OpcodeDecodeResult DecodeOpcode(byte opcode, ReadOnlySpan<byte> source, int sourcePointer, int sourceLength)
    {

        // Classify opcode
        if (opcode >= LzvnConstants.LiteralOpcodeStart)
        {
            // Literal opcodes (0xe0-0xff)
            if (opcode == LzvnConstants.LargeLiteralOpcode)
            {
                // Large literal
                if (sourceLength <= 2)
                    return new OpcodeDecodeResult(-1, 0, 0, null, 2);

                return new OpcodeDecodeResult(1, (nuint)source[sourcePointer + 1] + LzvnConstants.LargeLiteralBias, 0, null, 2);
            }

            if (opcode == LzvnConstants.LargeMatchOpcode)
            {
                // Large match (uses previous distance, 2 bytes)
                if (sourceLength <= 2)
                    return new OpcodeDecodeResult(-1, 0, 0, null, 2);

                return new OpcodeDecodeResult(1, 0, (nuint)source[sourcePointer + 1] + LzvnConstants.LargeMatchBias, null, 2);
            }

            if (opcode >= LzvnConstants.SmallMatchOpcodeStart)
            {
                // Small match (uses previous distance, 1 byte)
                if (sourceLength <= 1)
                    return new OpcodeDecodeResult(-1, 0, 0, null, 1);

                return new OpcodeDecodeResult(1, 0, (nuint)(opcode & 0x0f), null, 1);
            }

            // Small literal
            return new OpcodeDecodeResult(1, (nuint)(opcode & 0x0f), 0, null, 1);
        }

        if (opcode >= LzvnConstants.MediumDistanceOpcodeStart && opcode < LzvnConstants.MediumDistanceOpcodeEnd)
        {
            // Medium distance
            if (sourceLength <= 3 + (int)((opcode >> 3) & 3))
                return new OpcodeDecodeResult(-1, (nuint)((opcode >> 3) & 3), 0, null, 3);

            ushort nextTwoBytes = MemoryOperations.Load2(source[(sourcePointer + 1)..]);
            return new OpcodeDecodeResult(
                1,
                (nuint)((opcode >> 3) & 3),
                (nuint)(((opcode & 7) << 2) | (nextTwoBytes & 3)) + LzvnConstants.MatchLengthBias,
                (nuint)((nextTwoBytes >> 2) & 0x3fff),
                3);
        }

        if ((opcode & 7) == LzvnConstants.PreviousDistanceFlag)
        {
            // Previous distance
            if (sourceLength <= 1 + (int)(opcode >> 6))
                return new OpcodeDecodeResult(-1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, null, 1);

            return new OpcodeDecodeResult(1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, null, 1);
        }

        if ((opcode & 7) == LzvnConstants.LargeDistanceFlag)
        {
            // Large distance
            if (sourceLength <= 3 + (int)(opcode >> 6))
                return new OpcodeDecodeResult(-1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, null, 3);

            return new OpcodeDecodeResult(1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, MemoryOperations.Load2(source[(sourcePointer + 1)..]), 3);
        }

        if (opcode == LzvnConstants.EndOfStreamOpcode)
        {
            // End of stream
            return new OpcodeDecodeResult(-2, 0, 0, null, LzvnConstants.EndOfStreamOpcodeLength);
        }

        if (opcode == LzvnConstants.NopOpcode1 || opcode == LzvnConstants.NopOpcode2)
        {
            // NOP
            if (sourceLength <= 1)
                return new OpcodeDecodeResult(-1, 0, 0, null, 1);

            return new OpcodeDecodeResult(0, 0, 0, null, 1);
        }

        // Small distance
        if (sourceLength <= 2 + (int)(opcode >> 6))
            return new OpcodeDecodeResult(-1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, null, 2);

        return new OpcodeDecodeResult(1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, (nuint)(((opcode & 7) << 8) | source[sourcePointer + 1]), 2);
    }
}
