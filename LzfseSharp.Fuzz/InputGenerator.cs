using System;

namespace LzfseSharp.Fuzz;

/// <summary>
/// Generates a mix of input shapes for the in-process fuzzer. The generator is
/// not coverage-guided, so the distribution matters: shapes are chosen to
/// exercise block-dispatch, header-validation, single-block decode loops, and
/// multi-block framing. Random bytes alone almost always land on "Malformed"
/// at the block-magic check and explore very little of the decoder.
///
/// Size distribution is biased toward short inputs (where most bounds bugs
/// live) with a long tail up to 16 KB (to exercise LMD payload handling).
/// </summary>
internal static class InputGenerator
{
    // LZFSE block magic numbers from src/lzfse_internal.h.
    private const uint BvxEndMagic = 0x24787662;           // bvx$
    private const uint BvxUncompressedMagic = 0x2d787662;  // bvx-
    private const uint BvxLzvnMagic = 0x6e787662;          // bvxn
    private const uint BvxV2Magic = 0x32787662;            // bvx2
    private const uint BvxV1Magic = 0x31787662;            // bvx1

    // A minimal, known-valid bvxn stream that decodes to "X". The starting
    // point for mutation-based shapes.
    private static readonly byte[] MinimalBvxn =
    [
        0x62, 0x76, 0x78, 0x6e,          // bvxn magic
        0x01, 0x00, 0x00, 0x00,          // n_raw_bytes = 1
        0x0a, 0x00, 0x00, 0x00,          // n_payload_bytes = 10
        0xe1, (byte)'X',                 // sml_l L=1, literal 'X'
        0x06, 0, 0, 0, 0, 0, 0, 0,       // EOS marker
        0x62, 0x76, 0x78, 0x24,          // bvx$ end of stream
    ];

    public static byte[] Generate(Random rng)
    {
        // Weighted shape selection. Weights favour shape mixes that historically
        // find bugs (tiny-random, mutated-valid, multi-block) over shapes that
        // almost always bail at the magic check (large random).
        int choice = rng.Next(100);
        return choice switch
        {
            < 25 => TinyRandomBytes(rng),                          // 0..24:  boundary lengths
            < 35 => LargeRandomBytes(rng),                         // 25..34: multi-KB random
            < 45 => WithMagic(rng, PickMagic(rng), ShortTail(rng)),// 35..44: magic + short tail
            < 55 => WithMagic(rng, PickMagic(rng), MediumTail(rng)),//55..54: magic + medium tail
            < 75 => MutatedValidStream(rng),                       // 55..74: bit-flipped valid
            < 85 => ValidHeaderThenGarbage(rng),                   // 75..84: valid header, random body
            _    => MultiBlockStream(rng),                         // 85..99: 2–4 concatenated blocks
        };
    }

    /// <summary>
    /// Biased toward the sub-32-byte range where most bounds bugs live.
    /// </summary>
    private static byte[] TinyRandomBytes(Random rng)
    {
        int length = rng.Next(100) switch
        {
            < 10 => rng.Next(0, 4),       // near-empty (magic boundaries)
            < 40 => rng.Next(4, 16),      // truncated-header range
            < 80 => rng.Next(16, 64),     // short block range
            _    => rng.Next(64, 256),    // small block range
        };
        return RandomBytes(rng, length);
    }

    /// <summary>
    /// 256 B – 2 KB of random bytes. Longer than <see cref="TinyRandomBytes"/>
    /// (to exercise multi-block dispatch) but capped so the fuzzer's throughput
    /// stays usable — inputs that pass the magic check and enter decode loops
    /// dominate runtime.
    /// </summary>
    private static byte[] LargeRandomBytes(Random rng) => RandomBytes(rng, rng.Next(256, 2048));

    private static int ShortTail(Random rng) => rng.Next(0, 64);
    private static int MediumTail(Random rng) => rng.Next(64, 512);

    private static uint PickMagic(Random rng) => rng.Next(5) switch
    {
        0 => BvxEndMagic,
        1 => BvxUncompressedMagic,
        2 => BvxLzvnMagic,
        3 => BvxV1Magic,
        _ => BvxV2Magic,
    };

    private static byte[] RandomBytes(Random rng, int length)
    {
        byte[] b = new byte[length];
        rng.NextBytes(b);
        return b;
    }

    private static byte[] WithMagic(Random rng, uint magic, int tailLength)
    {
        byte[] b = new byte[4 + tailLength];
        BitConverter.GetBytes(magic).CopyTo(b, 0);
        rng.NextBytes(b.AsSpan(4));
        return b;
    }

    /// <summary>
    /// Minimal valid bvxn stream, mutated 1–32 times with a mix of XOR bit-flips,
    /// random byte replacement, and single-byte deletions. The varied mutation
    /// count explores both "almost valid" and "heavily corrupted" regimes.
    /// </summary>
    private static byte[] MutatedValidStream(Random rng)
    {
        byte[] stream = (byte[])MinimalBvxn.Clone();

        // Occasionally truncate before mutating — exercises "valid prefix, missing tail".
        if (rng.Next(10) == 0)
        {
            int truncateTo = rng.Next(4, stream.Length);
            Array.Resize(ref stream, truncateTo);
        }

        int mutations = rng.Next(1, 32);
        for (int i = 0; i < mutations; i++)
        {
            if (stream.Length == 0) break;
            int idx = rng.Next(stream.Length);
            switch (rng.Next(3))
            {
                case 0: // XOR bit flip
                    stream[idx] ^= (byte)(1 << rng.Next(8));
                    break;
                case 1: // full byte replacement
                    stream[idx] = (byte)rng.Next(256);
                    break;
                default: // pair swap
                    int other = rng.Next(stream.Length);
                    (stream[idx], stream[other]) = (stream[other], stream[idx]);
                    break;
            }
        }
        return stream;
    }

    /// <summary>
    /// Constructs a block with a valid header (correct magic, plausible lengths)
    /// followed by random payload bytes. Gets past the outer dispatch check and
    /// into the block decoder, where most opcode-loop and LMD-payload bugs live.
    /// </summary>
    private static byte[] ValidHeaderThenGarbage(Random rng)
    {
        return rng.Next(3) switch
        {
            0 => UncompressedWithRandomPayload(rng),
            1 => LzvnWithRandomPayload(rng),
            _ => V2WithRandomFreqAndPayload(rng),
        };
    }

    private static byte[] UncompressedWithRandomPayload(Random rng)
    {
        int rawBytes = rng.Next(0, 256);
        byte[] stream = new byte[8 + rawBytes + 4];
        BitConverter.GetBytes(BvxUncompressedMagic).CopyTo(stream, 0);
        BitConverter.GetBytes((uint)rawBytes).CopyTo(stream, 4);
        rng.NextBytes(stream.AsSpan(8, rawBytes));
        BitConverter.GetBytes(BvxEndMagic).CopyTo(stream, 8 + rawBytes);
        return stream;
    }

    private static byte[] LzvnWithRandomPayload(Random rng)
    {
        int rawBytes = rng.Next(0, 256);
        int payloadBytes = rng.Next(0, 128);
        byte[] stream = new byte[12 + payloadBytes + 4];
        BitConverter.GetBytes(BvxLzvnMagic).CopyTo(stream, 0);
        BitConverter.GetBytes((uint)rawBytes).CopyTo(stream, 4);
        BitConverter.GetBytes((uint)payloadBytes).CopyTo(stream, 8);
        rng.NextBytes(stream.AsSpan(12, payloadBytes));
        BitConverter.GetBytes(BvxEndMagic).CopyTo(stream, 12 + payloadBytes);
        return stream;
    }

    /// <summary>
    /// V2 block with a plausibly-sized header + small random payload. Forces the
    /// freq-table decoder into its bit-accumulator loop on arbitrary input.
    /// </summary>
    private static byte[] V2WithRandomFreqAndPayload(Random rng)
    {
        int headerSize = rng.Next(32, 128);      // untrusted; decoder must validate
        int payloadBytes = rng.Next(0, 128);
        byte[] stream = new byte[headerSize + payloadBytes + 4];
        rng.NextBytes(stream);                    // fill first, then overwrite magic + header-size
        BitConverter.GetBytes(BvxV2Magic).CopyTo(stream, 0);
        // Overwrite the header_size field in packed_fields[2] bits [0:32] at offset 24.
        BitConverter.GetBytes((uint)headerSize).CopyTo(stream, 24);
        BitConverter.GetBytes(BvxEndMagic).CopyTo(stream, headerSize + payloadBytes);
        return stream;
    }

    /// <summary>
    /// Concatenates 2–4 random individual blocks (each without its own EOS) and
    /// appends a single bvx$ terminator. Exercises the block-dispatch state
    /// machine across transitions.
    /// </summary>
    private static byte[] MultiBlockStream(Random rng)
    {
        int blockCount = rng.Next(2, 5);
        byte[][] blocks = new byte[blockCount][];
        int total = 4; // for EOS
        for (int i = 0; i < blockCount; i++)
        {
            blocks[i] = rng.Next(3) switch
            {
                0 => UncompressedBlock(rng),
                1 => LzvnBlock(rng),
                _ => WithMagic(rng, PickMagic(rng), ShortTail(rng)),
            };
            total += blocks[i].Length;
        }

        byte[] stream = new byte[total];
        int offset = 0;
        foreach (byte[] block in blocks)
        {
            block.CopyTo(stream, offset);
            offset += block.Length;
        }
        BitConverter.GetBytes(BvxEndMagic).CopyTo(stream, offset);
        return stream;
    }

    private static byte[] UncompressedBlock(Random rng)
    {
        int rawBytes = rng.Next(0, 64);
        byte[] block = new byte[8 + rawBytes];
        BitConverter.GetBytes(BvxUncompressedMagic).CopyTo(block, 0);
        BitConverter.GetBytes((uint)rawBytes).CopyTo(block, 4);
        rng.NextBytes(block.AsSpan(8));
        return block;
    }

    private static byte[] LzvnBlock(Random rng)
    {
        int rawBytes = rng.Next(0, 64);
        int payloadBytes = rng.Next(0, 128);
        byte[] block = new byte[12 + payloadBytes];
        BitConverter.GetBytes(BvxLzvnMagic).CopyTo(block, 0);
        BitConverter.GetBytes((uint)rawBytes).CopyTo(block, 4);
        BitConverter.GetBytes((uint)payloadBytes).CopyTo(block, 8);
        rng.NextBytes(block.AsSpan(12));
        return block;
    }
}
