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
    private static void Copy(Span<byte> dst, ReadOnlySpan<byte> src, int length)
    {
        int dstIdx = 0;
        int srcIdx = 0;
        int end = (length / 8) * 8;

        // Copy 8 bytes at a time
        while (dstIdx < end)
        {
            MemoryOperations.Copy8(dst[dstIdx..], src[srcIdx..]);
            dstIdx += 8;
            srcIdx += 8;
        }

        // Copy remaining bytes
        while (dstIdx < length)
        {
            dst[dstIdx] = src[srcIdx];
            dstIdx++;
            srcIdx++;
        }
    }

    /// <summary>
    /// Decode L, M, D triplets and expand matches
    /// </summary>
    public static int DecodeLmd(ref LzfseDecoderState s)
    {
        ref LzfseCompressedBlockDecoderState bs = ref s.CompressedLzfseBlockState;
        ushort lState = bs.LState;
        ushort mState = bs.MState;
        ushort dState = bs.DState;
        FseInStream inStream = bs.LmdInStream;
        int srcStart = s.SrcBegin;
        int src = s.Src + bs.LmdInBuf;
        int lit = bs.CurrentLiteralPos;
        int dst = s.Dst;
        uint symbols = bs.NMatches;
        int L = bs.LValue;
        int M = bs.MValue;
        int D = bs.DValue;

        // Remaining bytes in destination with safety margin
        long remainingBytes = s.DstEnd - dst - 32;

        // If L or M is non-zero, we have a pending match to complete
        if (L != 0 || M != 0)
        {
            if (!ExecuteMatch(s, ref bs, ref dst, ref lit, ref L, ref M, ref D, ref remainingBytes))
                goto SaveStateAndReturn;
        }

        while (symbols > 0)
        {
            // Decode next L, M, D triplet
            int res = inStream.Flush(ref src, srcStart, s.SrcBuffer);
            if (res != 0)
                return Constants.StatusError;

            L = FseDecoder.ValueDecode(ref lState, bs.LDecoder, ref inStream);

            if (lit + L >= bs.Literals.Length)
                return Constants.StatusError;

            res = inStream.Flush(ref src, srcStart, s.SrcBuffer);
            if (res != 0)
                return Constants.StatusError;

            M = FseDecoder.ValueDecode(ref mState, bs.MDecoder, ref inStream);

            res = inStream.Flush(ref src, srcStart, s.SrcBuffer);
            if (res != 0)
                return Constants.StatusError;

            int newD = FseDecoder.ValueDecode(ref dState, bs.DDecoder, ref inStream);
            D = newD != 0 ? newD : D;
            symbols--;

            if (!ExecuteMatch(s, ref bs, ref dst, ref lit, ref L, ref M, ref D, ref remainingBytes))
                goto SaveStateAndReturn;
        }

        // Block complete
        s.Dst = dst;
        return Constants.StatusOk;

    SaveStateAndReturn:
        // Save state for resumption
        bs.LValue = L;
        bs.MValue = M;
        bs.DValue = D;
        bs.LState = lState;
        bs.MState = mState;
        bs.DState = dState;
        bs.LmdInStream = inStream;
        bs.NMatches = symbols;
        bs.LmdInBuf = src - s.Src;
        bs.CurrentLiteralPos = lit;
        s.Dst = dst;
        return Constants.StatusDstFull;
    }

    private static bool ExecuteMatch(
        LzfseDecoderState s,
        ref LzfseCompressedBlockDecoderState bs,
        ref int dst,
        ref int lit,
        ref int L,
        ref int M,
        ref int D,
        ref long remainingBytes)
    {
        // Validate match distance
        if ((uint)D > dst + L - s.DstBegin)
            return false; // Invalid distance

        if (L + M <= remainingBytes)
        {
            // Fast path: plenty of space remaining
            remainingBytes -= L + M;

            // Copy literal
            Copy(s.DstBuffer[dst..], bs.Literals.AsSpan()[lit..], L);
            dst += L;
            lit += L;

            // Copy match
            if (D >= 8 || D >= M)
            {
                Copy(s.DstBuffer[dst..], s.DstBuffer[(dst - D)..], M);
            }
            else
            {
                for (int i = 0; i < M; i++)
                    s.DstBuffer[dst + i] = s.DstBuffer[dst + i - D];
            }
            dst += M;
            return true;
        }
        else
        {
            // Slow path: near end of buffer
            remainingBytes += 32;

            // Copy literal
            if (L <= remainingBytes)
            {
                for (int i = 0; i < L; i++)
                    s.DstBuffer[dst + i] = bs.Literals[lit + i];
                dst += L;
                lit += L;
                remainingBytes -= L;
                L = 0;
            }
            else
            {
                for (int i = 0; i < remainingBytes; i++)
                    s.DstBuffer[dst + i] = bs.Literals[lit + i];
                dst += (int)remainingBytes;
                lit += (int)remainingBytes;
                L -= (int)remainingBytes;
                return false; // Destination buffer is full
            }

            // Copy match
            if (M <= remainingBytes)
            {
                for (int i = 0; i < M; i++)
                    s.DstBuffer[dst + i] = s.DstBuffer[dst + i - D];
                dst += M;
                remainingBytes -= M;
                M = 0;
            }
            else
            {
                for (int i = 0; i < remainingBytes; i++)
                    s.DstBuffer[dst + i] = s.DstBuffer[dst + i - D];
                dst += (int)remainingBytes;
                M -= (int)remainingBytes;
                return false; // Destination buffer is full
            }

            remainingBytes -= 32;
            return true;
        }
    }
}
