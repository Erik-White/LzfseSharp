using System;

namespace LzfseSharp.Fuzz;

/// <summary>
/// Generates a mix of input shapes for the smoke-mode fuzzer. The mix is
/// designed to exercise block-dispatch, header-validation, and opcode-loop
/// paths — not just raw random bytes, which almost always land on "Malformed"
/// at the block-magic check.
/// </summary>
internal static class InputGenerator
{
    // LZFSE block magic numbers from src/lzfse_internal.h.
    private const uint BvxEndMagic = 0x24787662;           // bvx$
    private const uint BvxUncompressedMagic = 0x2d787662;  // bvx-
    private const uint BvxLzvnMagic = 0x6e787662;          // bvxn
    private const uint BvxV2Magic = 0x32787662;            // bvx2

    public static byte[] Generate(Random rng)
    {
        int shape = rng.Next(6);
        return shape switch
        {
            0 => RandomBytes(rng, rng.Next(0, 256)),
            1 => WithMagic(rng, BvxEndMagic, tailLength: 0),
            2 => WithMagic(rng, BvxUncompressedMagic, rng.Next(0, 64)),
            3 => WithMagic(rng, BvxLzvnMagic, rng.Next(0, 128)),
            4 => WithMagic(rng, BvxV2Magic, rng.Next(0, 256)),
            _ => MutatedValidStream(rng),
        };
    }

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
    /// Minimal valid bvxn stream, then 1–3 bit-flips. Explores near-valid inputs
    /// that random bytes essentially never produce.
    /// </summary>
    private static byte[] MutatedValidStream(Random rng)
    {
        byte[] stream =
        [
            0x62, 0x76, 0x78, 0x6e,          // bvxn magic
            0x01, 0x00, 0x00, 0x00,          // n_raw_bytes = 1
            0x0a, 0x00, 0x00, 0x00,          // n_payload_bytes = 10 (opcode + literal + EOS)
            0xe1, (byte)'X',                 // sml_l L=1, literal 'X'
            0x06, 0, 0, 0, 0, 0, 0, 0,       // EOS marker
            0x62, 0x76, 0x78, 0x24,          // bvx$ end of stream
        ];

        int mutations = rng.Next(1, 4);
        for (int i = 0; i < mutations; i++)
        {
            int idx = rng.Next(stream.Length);
            stream[idx] ^= (byte)(1 << rng.Next(8));
        }
        return stream;
    }
}
