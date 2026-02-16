using System.Runtime.CompilerServices;

namespace LzfseSharp.Fse;

/// <summary>
/// FSE decoder implementation
/// </summary>
internal static class FseDecoder
{
    /// <summary>
    /// Decode symbol using the decoder table and update state
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte Decode(ref ushort state, ReadOnlySpan<int> decoderTable, ref FseInStream inStream)
    {
        int entry = decoderTable[state];
        // Update state from K bits of input + DELTA
        state = (ushort)((entry >> 16) + (int)inStream.Pull(entry & 0xff));
        // Return the symbol for this state (bits 8-15)
        return (byte)((entry >> 8) & 0xff);
    }

    /// <summary>
    /// Decode value using the value decoder table and update state
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ValueDecode(ref ushort state, ReadOnlySpan<FseValueDecoderEntry> valueDecoderTable, ref FseInStream inStream)
    {
        FseValueDecoderEntry entry = valueDecoderTable[state];
        uint stateAndValueBits = (uint)inStream.Pull(entry.TotalBits);
        state = (ushort)(entry.Delta + (stateAndValueBits >> entry.ValueBits));
        return entry.VBase + (int)Core.BitOperations.ExtractBits(stateAndValueBits, 0, entry.ValueBits);
    }

    /// <summary>
    /// Check frequency table validity
    /// </summary>
    public static int CheckFreq(ReadOnlySpan<ushort> freqTable, int tableSize, int numberOfStates)
    {
        int sumOfFreq = 0;
        for (int i = 0; i < tableSize; i++)
        {
            sumOfFreq += freqTable[i];
        }
        return sumOfFreq > numberOfStates ? -1 : 0;
    }

    /// <summary>
    /// Initialize FSE decoder table
    /// </summary>
    public static int InitDecoderTable(int nstates, int nsymbols, ReadOnlySpan<ushort> freq, Span<int> table)
    {
        int nClz = Core.BitOperations.CountLeadingZeros((uint)nstates);
        int sumOfFreq = 0;

        int tableIndex = 0;
        for (int i = 0; i < nsymbols; i++)
        {
            int f = freq[i];
            if (f == 0)
                continue; // skip this symbol, no occurrences

            sumOfFreq += f;
            if (sumOfFreq > nstates)
                return -1;

            int k = Core.BitOperations.CountLeadingZeros((uint)f) - nClz;
            int j0 = ((2 * nstates) >> k) - f;

            // Initialize all states reached by this symbol
            for (int j = 0; j < f; j++)
            {
                int entryK, entryDelta;
                if (j < j0)
                {
                    entryK = k;
                    entryDelta = ((f + j) << k) - nstates;
                }
                else
                {
                    entryK = k - 1;
                    entryDelta = (j - j0) << (k - 1);
                }

                // Pack entry: [delta:16][symbol:8][k:8]
                int entry = (entryDelta << 16) | (i << 8) | (entryK & 0xff);
                table[tableIndex++] = entry;
            }
        }

        return 0; // OK
    }

    /// <summary>
    /// Initialize FSE value decoder table
    /// </summary>
    public static void InitValueDecoderTable(
        int nstates,
        int nsymbols,
        ReadOnlySpan<ushort> freq,
        ReadOnlySpan<byte> symbolVBits,
        ReadOnlySpan<int> symbolVBase,
        Span<FseValueDecoderEntry> table)
    {
        int nClz = Core.BitOperations.CountLeadingZeros((uint)nstates);

        int tableIndex = 0;
        for (int i = 0; i < nsymbols; i++)
        {
            int f = freq[i];
            if (f == 0)
                continue; // skip this symbol, no occurrences

            int k = Core.BitOperations.CountLeadingZeros((uint)f) - nClz;
            int j0 = ((2 * nstates) >> k) - f;

            FseValueDecoderEntry baseEntry = new FseValueDecoderEntry
            {
                ValueBits = symbolVBits[i],
                VBase = symbolVBase[i]
            };

            // Initialize all states reached by this symbol
            for (int j = 0; j < f; j++)
            {
                FseValueDecoderEntry entry = baseEntry;

                if (j < j0)
                {
                    entry.TotalBits = (byte)(k + entry.ValueBits);
                    entry.Delta = (short)(((f + j) << k) - nstates);
                }
                else
                {
                    entry.TotalBits = (byte)(k - 1 + entry.ValueBits);
                    entry.Delta = (short)((j - j0) << (k - 1));
                }

                table[tableIndex++] = entry;
            }
        }
    }
}
