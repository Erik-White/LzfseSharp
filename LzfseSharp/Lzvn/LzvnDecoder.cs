using LzfseSharp.Core;
using System.Runtime.CompilerServices;

namespace LzfseSharp.Lzvn;

/// <summary>
/// LZVN decoder state
/// </summary>
internal struct LzvnDecoderState
{
    public int SrcPos;
    public int SrcEnd;
    public int DstPos;
    public int DstBegin;
    public int DstEnd;
    public nuint L, M, D;
    public int DPrev;
    public bool EndOfStream;
}

/// <summary>
/// LZVN low-level decoder
/// </summary>
internal static class LzvnDecoder
{
    /// <summary>
    /// Decode LZVN compressed data
    /// </summary>
    public static void Decode(ref LzvnDecoderState state, ReadOnlySpan<byte> srcBuffer, Span<byte> dstBuffer)
    {
        int srcLen = state.SrcEnd - state.SrcPos;
        int dstLen = state.DstEnd - state.DstPos;
        if (srcLen == 0 || dstLen == 0)
            return;

        int srcPtr = state.SrcPos;
        int dstPtr = state.DstPos;
        nuint D = (nuint)state.DPrev;
        nuint M, L;
        int opcLen = 0;

        // Handle partially expanded match saved in state
        if (state.L != 0 || state.M != 0)
        {
            L = state.L;
            M = state.M;
            D = state.D;
            opcLen = 0;
            state.L = state.M = state.D = 0;

            if (M == 0)
            {
                if (!ProcessLiteral(dstBuffer, ref dstPtr, srcBuffer, ref srcPtr, L, ref dstLen, ref srcLen, D, out var partialState1))
                {
                    state = partialState1;
                    return;
                }
            }
            else if (L == 0)
            {
                if (!ProcessMatch(dstBuffer, ref dstPtr, M, D, ref dstLen, srcBuffer, ref srcPtr, 0, ref srcLen, out var partialState2))
                {
                    state = partialState2;
                    return;
                }
            }
            else
            {
                if (!ProcessLiteralAndMatch(dstBuffer, ref dstPtr, srcBuffer, ref srcPtr, L, M, D, state.DstBegin, ref dstLen, ref srcLen, out var partialState3))
                {
                    state = partialState3;
                    return;
                }
            }

            D = state.D;
        }

        while (srcPtr < state.SrcEnd)
        {
            byte opc = srcBuffer[srcPtr];

            // Decode opcode and extract L, M, D values
            int opResult = DecodeOpcode(opc, srcBuffer, srcPtr, srcLen, out L, out M, ref D, out opcLen);

            if (opResult == -1)
                break; // Source truncated

            if (opResult == -2)
            {
                // End of stream
                state.SrcPos = srcPtr + opcLen;
                state.EndOfStream = true;
                state.DstPos = dstPtr;
                state.DPrev = (int)D;
                return;
            }

            if (opResult == 0)
            {
                // NOP, skip and continue
                srcPtr += opcLen;
                srcLen -= opcLen;
                continue;
            }

            // opResult == 1: normal opcode
            if (L > 0 && M > 0)
            {
                // Literal and match
                if (!ProcessLiteralAndMatch(dstBuffer, ref dstPtr, srcBuffer, ref srcPtr, L, M, D, state.DstBegin, ref dstLen, ref srcLen, out var partialState))
                {
                    state = partialState;
                    state.D = D;
                    return;
                }
            }
            else if (L > 0)
            {
                // Literal only
                if (!ProcessLiteral(dstBuffer, ref dstPtr, srcBuffer, ref srcPtr, L, ref dstLen, ref srcLen, D, out var partialState))
                {
                    state = partialState;
                    return;
                }
            }
            else if (M > 0)
            {
                // Match only
                if (!ProcessMatch(dstBuffer, ref dstPtr, M, D, ref dstLen, srcBuffer, ref srcPtr, opcLen, ref srcLen, out var partialState))
                {
                    state = partialState;
                    return;
                }
            }
            else
            {
                // Neither L nor M, just skip the opcode
                srcPtr += opcLen;
                srcLen -= opcLen;
            }
        }
        state.SrcPos = srcPtr;
        state.DstPos = dstPtr;
        state.DPrev = (int)D;
    }

    private static bool ProcessLiteralAndMatch(
        Span<byte> dst, ref int dstPtr,
        ReadOnlySpan<byte> src, ref int srcPtr,
        nuint L, nuint M, nuint D, int dstBegin,
        ref int dstLen, ref int srcLen,
        out LzvnDecoderState partialState)
    {
        partialState = default;

        // Advance past opcode
        int opcLen = srcPtr < src.Length ? (src[srcPtr] >= 0xe0 && src[srcPtr] < 0xf0 ? (src[srcPtr] == 0xe0 ? 2 : 1) :
                      (src[srcPtr] >= 0xa0 && src[srcPtr] < 0xc0 ? 3 : ((src[srcPtr] & 7) == 6 ? 1 : ((src[srcPtr] & 7) == 7 ? 3 : 2)))) : 1;
        srcPtr += opcLen;
        srcLen -= opcLen;

        // Copy literal
        if (!CopyLiteralBytes(dst, ref dstPtr, src, ref srcPtr, L, dstLen, ref srcLen))
        {
            partialState.SrcPos = srcPtr;
            partialState.DstPos = dstPtr;
            partialState.L = L;
            partialState.M = M;
            partialState.D = D;
            return false;
        }

        dstLen -= (int)L;

        // Validate match distance
        if (D > (nuint)(dstPtr - dstBegin) || D == 0)
            return false;

        // Copy match
        if (!CopyMatchBytes(dst, ref dstPtr, M, D, dstLen))
        {
            partialState.SrcPos = srcPtr;
            partialState.DstPos = dstPtr;
            partialState.L = 0;
            partialState.M = M;
            partialState.D = D;
            return false;
        }

        dstLen -= (int)M;
        return true;
    }

    private static bool ProcessLiteral(
        Span<byte> dst, ref int dstPtr,
        ReadOnlySpan<byte> src, ref int srcPtr,
        nuint L, ref int dstLen, ref int srcLen,
        nuint D, out LzvnDecoderState partialState)
    {
        partialState = default;

        int opcLen = srcPtr < src.Length ? (src[srcPtr] >= 0xe0 && src[srcPtr] < 0xf0 ? (src[srcPtr] == 0xe0 ? 2 : 1) : 1) : 1;
        srcPtr += opcLen;
        srcLen -= opcLen;

        if (!CopyLiteralBytes(dst, ref dstPtr, src, ref srcPtr, L, dstLen, ref srcLen))
        {
            partialState.SrcPos = srcPtr;
            partialState.DstPos = dstPtr;
            partialState.L = L;
            partialState.M = 0;
            partialState.D = D;
            return false;
        }

        dstLen -= (int)L;
        return true;
    }

    private static bool ProcessMatch(
        Span<byte> dst, ref int dstPtr,
        nuint M, nuint D, ref int dstLen,
        ReadOnlySpan<byte> src, ref int srcPtr,
        int opcLen, ref int srcLen,
        out LzvnDecoderState partialState)
    {
        partialState = default;

        if (opcLen > 0)
        {
            srcPtr += opcLen;
            srcLen -= opcLen;
        }

        if (!CopyMatchBytes(dst, ref dstPtr, M, D, dstLen))
        {
            partialState.SrcPos = srcPtr;
            partialState.DstPos = dstPtr;
            partialState.L = 0;
            partialState.M = M;
            partialState.D = D;
            return false;
        }

        dstLen -= (int)M;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CopyLiteralBytes(Span<byte> dst, ref int dstPtr, ReadOnlySpan<byte> src, ref int srcPtr, nuint L, int dstLen, ref int srcLen)
    {
        if (dstLen >= 4 && srcLen >= 4 && L <= 3)
        {
            MemoryOperations.Store4(dst[dstPtr..], MemoryOperations.Load4(src[srcPtr..]));
        }
        else if (L <= (nuint)dstLen && L <= (nuint)srcLen)
        {
            for (nuint i = 0; i < L; i++)
                dst[dstPtr + (int)i] = src[srcPtr + (int)i];
        }
        else if (L > (nuint)dstLen)
        {
            for (int i = 0; i < dstLen; i++)
                dst[dstPtr + i] = src[srcPtr + i];
            srcPtr += dstLen;
            srcLen -= dstLen;
            return false;
        }
        else
        {
            return false;
        }

        dstPtr += (int)L;
        srcPtr += (int)L;
        srcLen -= (int)L;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CopyMatchBytes(Span<byte> dst, ref int dstPtr, nuint M, nuint D, int dstLen)
    {
        // Fast path: 8-byte copies when distance is at least 8
        // This handles overlapping copies correctly by reading most recent writes
        if (dstLen >= (int)(M + 7) && D >= 8)
        {
            for (nuint i = 0; i < M; i += 8)
                MemoryOperations.Store8(dst[(dstPtr + (int)i)..], MemoryOperations.Load8(dst[(dstPtr + (int)i - (int)D)..]));
        }
        else if (M <= (nuint)dstLen)
        {
            for (nuint i = 0; i < M; i++)
                dst[dstPtr + (int)i] = dst[dstPtr + (int)i - (int)D];
        }
        else
        {
            for (int i = 0; i < dstLen; i++)
                dst[dstPtr + i] = dst[dstPtr + i - (int)D];
            return false;
        }

        dstPtr += (int)M;
        return true;
    }

    private static int DecodeOpcode(byte opc, ReadOnlySpan<byte> src, int srcPtr, int srcLen, out nuint L, out nuint M, ref nuint D, out int opcLen)
    {
        L = M = 0;
        opcLen = 0;

        // Classify opcode
        if (opc >= 0xe0)
        {
            // Literal opcodes (0xe0-0xff)
            if (opc == 0xe0)
            {
                // Large literal
                opcLen = 2;
                if (srcLen <= 2) return -1;
                L = (nuint)src[srcPtr + 1] + 16;
                return 1;
            }
            else if (opc == 0xf0)
            {
                // Large match (uses previous distance, 2 bytes)
                opcLen = 2;
                if (srcLen <= 2) return -1;
                M = (nuint)src[srcPtr + 1] + 16;
                return 1;
            }
            else if (opc >= 0xf1)
            {
                // Small match (uses previous distance, 1 byte)
                opcLen = 1;
                if (srcLen <= 1) return -1;
                M = (nuint)(opc & 0x0f);
                return 1;
            }
            else
            {
                // Small literal
                opcLen = 1;
                L = (nuint)(opc & 0x0f);
                return 1;
            }
        }
        else if (opc >= 0xa0 && opc < 0xc0)
        {
            // Medium distance
            opcLen = 3;
            L = (nuint)((opc >> 3) & 3);
            if (srcLen <= opcLen + (int)L) return -1;
            ushort opc23 = MemoryOperations.Load2(src[(srcPtr + 1)..]);
            M = (nuint)(((opc & 7) << 2) | (opc23 & 3)) + 3;
            D = (nuint)((opc23 >> 2) & 0x3fff);
            return 1;
        }
        else if ((opc & 7) == 6)
        {
            // Previous distance
            opcLen = 1;
            L = (nuint)(opc >> 6);
            M = (nuint)((opc >> 3) & 7) + 3;
            if (srcLen <= opcLen + (int)L) return -1;
            return 1;
        }
        else if ((opc & 7) == 7)
        {
            // Large distance
            opcLen = 3;
            L = (nuint)(opc >> 6);
            M = (nuint)((opc >> 3) & 7) + 3;
            if (srcLen <= opcLen + (int)L) return -1;
            D = MemoryOperations.Load2(src[(srcPtr + 1)..]);
            return 1;
        }
        else if (opc == 6)
        {
            // End of stream
            opcLen = 8;
            return -2;
        }
        else if (opc == 14 || opc == 22)
        {
            // NOP
            opcLen = 1;
            if (srcLen <= 1) return -1;
            return 0;
        }
        else
        {
            // Small distance
            opcLen = 2;
            L = (nuint)(opc >> 6);
            M = (nuint)((opc >> 3) & 7) + 3;
            if (srcLen <= opcLen + (int)L) return -1;
            D = (nuint)(((opc & 7) << 8) | src[srcPtr + 1]);
            return 1;
        }
    }
}
