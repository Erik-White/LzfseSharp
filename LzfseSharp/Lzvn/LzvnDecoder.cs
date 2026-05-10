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
        nuint M;
        nuint L;
        int opcodeLength;

        // Handle partially expanded match saved in state. The opcode that produced
        // these pending L/M/D values was already consumed by the prior Decode call,
        // so sourcePosition already points past it — pass opcodeLength: 0 so the
        // Process* helpers do not advance over a phantom opcode.
        if (state.LiteralLength != 0 || state.MatchLength != 0)
        {
            L = state.LiteralLength;
            M = state.MatchLength;
            D = state.MatchDistance;
            state.LiteralLength = state.MatchLength = state.MatchDistance = 0;

            bool resumeOk;
            if (M == 0)
                resumeOk = ProcessLiteral(ref state, dstBuffer, ref destinationPosition, srcBuffer, ref sourcePosition, ref L, D, ref destinationLength, ref sourceLength, opcodeLength: 0);
            else if (L == 0)
                resumeOk = ProcessMatch(ref state, dstBuffer, ref destinationPosition, ref M, D, ref destinationLength, srcBuffer, ref sourcePosition, opcodeLength: 0, ref sourceLength);
            else
                resumeOk = ProcessLiteralAndMatch(ref state, dstBuffer, ref destinationPosition, srcBuffer, ref sourcePosition, ref L, ref M, D, ref destinationLength, ref sourceLength, opcodeLength: 0);

            if (!resumeOk)
                return;
        }

        while (sourcePosition < state.SourceEnd)
        {
            byte opcode = srcBuffer[sourcePosition];

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

            bool opOk;
            if (L > 0 && M > 0)
                opOk = ProcessLiteralAndMatch(ref state, dstBuffer, ref destinationPosition, srcBuffer, ref sourcePosition, ref L, ref M, D, ref destinationLength, ref sourceLength, opcodeLength);
            else if (L > 0)
                opOk = ProcessLiteral(ref state, dstBuffer, ref destinationPosition, srcBuffer, ref sourcePosition, ref L, D, ref destinationLength, ref sourceLength, opcodeLength);
            else if (M > 0)
                opOk = ProcessMatch(ref state, dstBuffer, ref destinationPosition, ref M, D, ref destinationLength, srcBuffer, ref sourcePosition, opcodeLength, ref sourceLength);
            else
            {
                // Neither L nor M, just skip the opcode
                sourcePosition += opcodeLength;
                sourceLength -= opcodeLength;
                continue;
            }

            if (!opOk)
                return;
        }

        state.SourcePosition = sourcePosition;
        state.DestinationPosition = destinationPosition;
        state.PreviousDistance = (int)D;
    }

    // The Process* and Copy* helpers below return true when the op completed and false
    // when the destination was exhausted mid-op. On false they update `state` with the
    // bookkeeping needed to resume (position + remaining L/M/D), leaving framing fields
    // (SourceEnd, DestinationStart/End, PreviousDistance, EndOfStream) untouched so the
    // caller can call Decode again without re-initialising them.

    private static bool ProcessLiteralAndMatch(
        ref LzvnDecoderState state,
        Span<byte> dst,
        ref int destinationPosition,
        ReadOnlySpan<byte> src,
        ref int sourcePosition,
        ref nuint L,
        ref nuint M,
        nuint D,
        ref int destinationLength,
        ref int sourceLength,
        int opcodeLength)
    {
        sourcePosition += opcodeLength;
        sourceLength -= opcodeLength;

        if (!CopyLiteralBytes(dst, ref destinationPosition, src, ref sourcePosition, ref L, ref destinationLength, ref sourceLength))
        {
            state.SourcePosition = sourcePosition;
            state.DestinationPosition = destinationPosition;
            state.LiteralLength = L;
            state.MatchLength = M;
            state.MatchDistance = D;
            return false;
        }

        // Validate match distance against output written so far.
        if (D > (nuint)(destinationPosition - state.DestinationStart) || D == 0)
            return false;

        if (!CopyMatchBytes(dst, ref destinationPosition, ref M, D, ref destinationLength))
        {
            state.SourcePosition = sourcePosition;
            state.DestinationPosition = destinationPosition;
            state.LiteralLength = 0;
            state.MatchLength = M;
            state.MatchDistance = D;
            return false;
        }

        return true;
    }

    private static bool ProcessLiteral(
        ref LzvnDecoderState state,
        Span<byte> destination,
        ref int destinationPosition,
        ReadOnlySpan<byte> source,
        ref int sourcePosition,
        ref nuint L,
        nuint D,
        ref int destinationLength,
        ref int sourceLength,
        int opcodeLength)
    {
        sourcePosition += opcodeLength;
        sourceLength -= opcodeLength;

        if (!CopyLiteralBytes(destination, ref destinationPosition, source, ref sourcePosition, ref L, ref destinationLength, ref sourceLength))
        {
            state.SourcePosition = sourcePosition;
            state.DestinationPosition = destinationPosition;
            state.LiteralLength = L;
            state.MatchLength = 0;
            state.MatchDistance = D;
            return false;
        }

        return true;
    }

    private static bool ProcessMatch(
        ref LzvnDecoderState state,
        Span<byte> destination,
        ref int destinationPosition,
        ref nuint M,
        nuint D,
        ref int destinationLength,
        ReadOnlySpan<byte> source,
        ref int sourcePosition,
        int opcodeLength,
        ref int sourceLength)
    {
        sourcePosition += opcodeLength;
        sourceLength -= opcodeLength;

        if (!CopyMatchBytes(destination, ref destinationPosition, ref M, D, ref destinationLength))
        {
            state.SourcePosition = sourcePosition;
            state.DestinationPosition = destinationPosition;
            state.LiteralLength = 0;
            state.MatchLength = M;
            state.MatchDistance = D;
            return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CopyLiteralBytes(Span<byte> dst, ref int destinationPosition, ReadOnlySpan<byte> src, ref int sourcePosition, ref nuint L, ref int destinationLength, ref int sourceLength)
    {
        // Two fast paths, mirroring the reference:
        //   * Small literals (L <= 3, from sml_d/med_d/lrg_d/pre_d opcodes): one 4-byte store.
        //   * Larger literals (L up to 271, from sml_l/lrg_l opcodes): 8-byte stride up to L.
        // Both fast paths can slop past the logical L bytes (store4 writes 4, store8 loop
        // can write up to L+7); the caller's outer framing ensures dst has enough slack.
        if (destinationLength >= 4 && sourceLength >= 4 && L <= 3)
        {
            MemoryOperations.Store4(dst[destinationPosition..], MemoryOperations.Load4(src[sourcePosition..]));
        }
        else if ((nuint)destinationLength >= L + 7 && (nuint)sourceLength >= L + 7)
        {
            for (nuint i = 0; i < L; i += 8)
                MemoryOperations.Store8(dst[(destinationPosition + (int)i)..], MemoryOperations.Load8(src[(sourcePosition + (int)i)..]));
        }
        else if (L <= (nuint)destinationLength && L <= (nuint)sourceLength)
        {
            for (nuint i = 0; i < L; i++)
                dst[destinationPosition + (int)i] = src[sourcePosition + (int)i];
        }
        else if (L > (nuint)destinationLength)
        {
            // Partial copy: fill remaining destination, return false with L reduced to
            // the bytes still outstanding.
            int copied = destinationLength;
            for (int i = 0; i < copied; i++)
                dst[destinationPosition + i] = src[sourcePosition + i];
            destinationPosition += copied;
            sourcePosition += copied;
            sourceLength -= copied;
            destinationLength = 0;
            L -= (nuint)copied;
            return false;
        }
        else
        {
            return false;
        }

        destinationPosition += (int)L;
        sourcePosition += (int)L;
        sourceLength -= (int)L;
        destinationLength -= (int)L;
        L = 0;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CopyMatchBytes(Span<byte> dst, ref int destinationPosition, ref nuint M, nuint D, ref int destinationLength)
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
            // Partial copy: fill remaining destination, leave M reduced to outstanding bytes.
            int copied = destinationLength;
            for (int i = 0; i < copied; i++)
                dst[destinationPosition + i] = dst[destinationPosition + i - (int)D];
            destinationPosition += copied;
            destinationLength = 0;
            M -= (nuint)copied;
            return false;
        }

        destinationPosition += (int)M;
        destinationLength -= (int)M;
        M = 0;
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
            if (sourceLength <= 3 + ((opcode >> 3) & 3))
                return new OpcodeDecodeResult(-1, (nuint)((opcode >> 3) & 3), 0, null, 3);

            ushort nextTwoBytes = MemoryOperations.Load2(source[(sourcePointer + 1)..]);
            return new OpcodeDecodeResult(
                1,
                (nuint)((opcode >> 3) & 3),
                (nuint)(((opcode & 7) << 2) | (nextTwoBytes & 3)) + LzvnConstants.MatchLengthBias,
                (nuint)((nextTwoBytes >> 2) & 0x3fff),
                3);
        }

        if (opcode == LzvnConstants.EndOfStreamOpcode)
        {
            // End of stream: the EOS marker is 8 bytes (0x06 + 7 padding).
            // Require the full marker to be present so a truncated EOS is not silently accepted.
            if (sourceLength < LzvnConstants.EndOfStreamOpcodeLength)
                return new OpcodeDecodeResult(-1, 0, 0, null, LzvnConstants.EndOfStreamOpcodeLength);

            return new OpcodeDecodeResult(-2, 0, 0, null, LzvnConstants.EndOfStreamOpcodeLength);
        }

        if (opcode == LzvnConstants.NopOpcode1 || opcode == LzvnConstants.NopOpcode2)
        {
            // NOP
            if (sourceLength <= 1)
                return new OpcodeDecodeResult(-1, 0, 0, null, 1);

            return new OpcodeDecodeResult(0, 0, 0, null, 1);
        }

        if ((opcode & 7) == LzvnConstants.PreviousDistanceFlag)
        {
            // Previous distance
            if (sourceLength <= 1 + (opcode >> 6))
                return new OpcodeDecodeResult(-1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, null, 1);

            return new OpcodeDecodeResult(1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, null, 1);
        }

        if ((opcode & 7) == LzvnConstants.LargeDistanceFlag)
        {
            // Large distance
            if (sourceLength <= 3 + (opcode >> 6))
                return new OpcodeDecodeResult(-1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, null, 3);

            return new OpcodeDecodeResult(1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, MemoryOperations.Load2(source[(sourcePointer + 1)..]), 3);
        }

        // Small distance
        if (sourceLength <= 2 + (opcode >> 6))
            return new OpcodeDecodeResult(-1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, null, 2);

        return new OpcodeDecodeResult(1, (nuint)(opcode >> 6), (nuint)((opcode >> 3) & 7) + LzvnConstants.MatchLengthBias, (nuint)(((opcode & 7) << 8) | source[sourcePointer + 1]), 2);
    }
}
