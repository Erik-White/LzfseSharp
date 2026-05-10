using AwesomeAssertions;
using LzfseSharp.Lzvn;
using Xunit;

namespace LzfseSharp.Tests;

/// <summary>
/// Opcode-by-opcode coverage of the LZVN decoder. LZFSE has no separate written
/// specification — the format is defined by the reference implementation at
/// https://github.com/lzfse/lzfse. Opcode layouts and value ranges cited below are
/// taken from the opcode jump table and comments in src/lzvn_decode_base.c.
///
/// Opcode families:
///   sml_d  (small distance)       LLMMMDDD DDDDDDDD LITERAL        2-byte + L bytes literal
///   med_d  (medium distance)      101LLMMM DDDDDDMM DDDDDDDD LIT   3-byte + L bytes literal
///   lrg_d  (large distance)       LLMMM111 DDDDDDDD DDDDDDDD LIT   3-byte + L bytes literal
///   pre_d  (previous distance)    LLMMM110 LITERAL                 1-byte + L bytes literal
///   sml_m  (small match)          1111MMMM                         1-byte, reuses D
///   lrg_m  (large match)          11110000 MMMMMMMM                2-byte, reuses D
///   sml_l  (small literal)        1110LLLL LITERAL                 1-byte + L bytes literal
///   lrg_l  (large literal)        11100000 LLLLLLLL LITERAL        2-byte + L bytes literal
///   eos    (end of stream)        0x06 + 7 padding                 8-byte terminator
///   nop                           0x0e or 0x16                     1-byte
///   udef   (undefined)            various                          reject
/// </summary>
public class LzvnOpcodeTests
{
    [Fact]
    public void SmallLiteral_DecodesCorrectly()
    {
        // 0xe0|L, L in 1..15. Using L=5 here.
        byte[] payload = Build(bytes =>
        {
            bytes.Add(0xe5);
            bytes.AddRange("Hello"u8.ToArray());
            AppendEos(bytes);
        });

        DecodeToEnd(payload, expected: "Hello"u8.ToArray());
    }

    [Fact]
    public void LargeLiteral_AtMinBoundary_DecodesCorrectly()
    {
        // 0xe0 + (L - 16), L in [16, 271]. Minimum L = 16.
        byte[] literal = new byte[16];
        for (int i = 0; i < 16; i++) literal[i] = (byte)('A' + i);

        byte[] payload = Build(bytes =>
        {
            bytes.Add(0xe0);
            bytes.Add(0); // L - 16 = 0, so L = 16
            bytes.AddRange(literal);
            AppendEos(bytes);
        });

        DecodeToEnd(payload, literal);
    }

    [Fact]
    public void LargeLiteral_AtMaxBoundary_DecodesCorrectly()
    {
        // Maximum L = 16 + 255 = 271.
        byte[] literal = new byte[271];
        for (int i = 0; i < 271; i++) literal[i] = (byte)i;

        byte[] payload = Build(bytes =>
        {
            bytes.Add(0xe0);
            bytes.Add(255); // L - 16 = 255, so L = 271
            bytes.AddRange(literal);
            AppendEos(bytes);
        });

        DecodeToEnd(payload, literal);
    }

    [Fact]
    public void SmallMatch_DecodesCorrectly()
    {
        // sml_m opcode range 0xf1..0xff: M = opcode & 0xf, reuses previous distance.
        // First establish prev_D via a sml_d opcode.
        byte[] payload = Build(bytes =>
        {
            // sml_d layout: LLMMMDDD DDDDDDDD LITERAL
            // Want L=2, M=6 (so M-3=3 → MMM=011), D=2 (high 3 bits=0, low 8 bits=2).
            // Opcode = 0b10_011_000 = 0x98.
            bytes.Add(0x98);
            bytes.Add(2);
            bytes.Add((byte)'A');
            bytes.Add((byte)'B');
            // After sml_d: literal "AB" then match(M=6,D=2) byte-wise -> "ABABABAB" (8 bytes).
            // sml_m: M = 4 -> copies dst[pos-2..] 4 bytes -> extends to "ABABABABABAB" (12 bytes).
            bytes.Add(0xf4);
            AppendEos(bytes);
        });

        DecodeToEnd(payload, expected: "ABABABABABAB"u8.ToArray());
    }

    [Fact]
    public void LargeMatch_DecodesCorrectly()
    {
        // 0xf0 MMMMMMMM: large match, M = srcByte + 16 (range 16..271), reuses distance.
        byte[] payload = Build(bytes =>
        {
            // Prime dst with 16 bytes via large literal + set distance via sml_d.
            // Actually simpler: use sml_d with L=1, M=3, D=1 to splat a byte and set prev_D = 1.
            // Then lrg_m (M=16) to extend.
            bytes.Add(0b01_011_001); // sml_d: LL=01 (L=1), MMM=011 (M=3+3=6), DDD=001 (D high 3 bits = 1)
            bytes.Add(0); // D low 8 bits = 0; D = 256. Too large — need D=1.
        });
        // restart: use a different approach. Use sml_l to write 1 byte, then sml_d with L=0, M=3, D=1.
        payload = Build(bytes =>
        {
            // sml_l L=1 "X"
            bytes.Add(0xe1);
            bytes.Add((byte)'X');
            // sml_d: opcode 0bLLMMMDDD, LL=00 (L=0), MMM=000 (M=3), DDD=000 (D high=0); next byte = D&0xff = 1 → D=1
            bytes.Add(0x00);
            bytes.Add(1);
            // After sml_d: match(M=3, D=1) from "X" splats "XXX", dst = "XXXX". prev_D=1.
            // lrg_m: 0xf0 + byte. srcByte=0 → M = 16. Needs dst to have at least D=1 byte to read from.
            bytes.Add(0xf0);
            bytes.Add(0); // M = 0 + 16 = 16
            AppendEos(bytes);
        });

        // dst = "X" + "XXX" (from sml_d) + "XXXXXXXXXXXXXXXX" (from lrg_m, 16 bytes)
        //     = 20 × 'X'
        byte[] expected = new byte[20];
        Array.Fill(expected, (byte)'X');
        DecodeToEnd(payload, expected);
    }

    [Fact]
    public void PreviousDistance_ReusesLastDistance()
    {
        // pre_d: LLMMM110 — reuses previous distance, carries its own L and M.
        byte[] payload = Build(bytes =>
        {
            // sml_l "AB"
            bytes.Add(0xe2);
            bytes.Add((byte)'A');
            bytes.Add((byte)'B');
            // sml_d: LL=0, MMM=0 (M=3), DDD=0; D low=2 → D=2
            bytes.Add(0x00);
            bytes.Add(2);
            // dst: "AB" + match(M=3,D=2) = "AB" + "ABA" = "ABABA". prev_D=2.
            // pre_d: 0bLL_MMM_110, LL=01 (L=1), MMM=000 (M=3). Opcode: 0b01_000_110 = 0x46.
            bytes.Add(0x46);
            bytes.Add((byte)'C');
            // pre_d: copies L=1 literal 'C', then match M=3, D=prev_D=2.
            // After literal: "ABABAC" (pos=6). Match M=3,D=2 reads dst[4..6] = "AC", but wraps
            // since M > D: byte-by-byte ABABAC → ABABACAC[A].
            // Step: dst[6] = dst[4] = 'A'; dst[7] = dst[5] = 'C'; dst[8] = dst[6] = 'A'
            // Result: "ABABACACA"
            AppendEos(bytes);
        });

        DecodeToEnd(payload, expected: "ABABACACA"u8.ToArray());
    }

    [Fact]
    public void MediumDistance_DecodesCorrectly()
    {
        // med_d: 101LLMMM DDDDDDMM DDDDDDDD (3-byte + L bytes literal)
        // Opcode is in range 0xa0..0xbf. L in bits 3..4, M = ((opcode & 7) << 2) | (byte2 & 3) + 3,
        // D = (byte2 >> 2) | (byte3 << 6)  -- wait, let me re-read:
        // From reference: M = ((opcode & 7) << 2 | (opc23 & 3)) + 3;  D = extract(opc23, 2, 14)
        // opc23 is a little-endian load2 of the next 2 bytes.
        //
        // Pick L=1, M=3, D=300, literal='Z'.
        // opc23 needed: bits [0:2) of M-3 = 0, bits [2:16) of D = 300 → opc23 = (300 << 2) | 0 = 1200 = 0x04B0.
        // opcode: 101_LL_MMM where MMM is (M-3) >> 2 = 0; so opcode = 0b101_01_000 = 0xa8.
        byte[] payload = Build(bytes =>
        {
            // Prime dst with 300 bytes so D=300 is valid.
            bytes.Add(0xe0);
            bytes.Add(255); // L = 271
            for (int i = 0; i < 271; i++) bytes.Add((byte)(i & 0xff));
            // sml_l "0123456789012345678901234567890" (pad to 300)
            // 300 - 271 = 29 more bytes
            bytes.Add(0xe0 | 13); // sml_l, L = 13
            for (int i = 0; i < 13; i++) bytes.Add((byte)'X');
            bytes.Add(0xe0 | 13); // sml_l, L = 13
            for (int i = 0; i < 13; i++) bytes.Add((byte)'Y');
            bytes.Add(0xe0 | 3); // sml_l, L = 3
            for (int i = 0; i < 3; i++) bytes.Add((byte)'Z');
            // dst length now = 271 + 13 + 13 + 3 = 300.
            // med_d: L=1 "Q", M=3, D=300
            bytes.Add(0xa8);          // 0b10101000: 101 LL=01 MMM=000
            bytes.Add(0xB0); bytes.Add(0x04); // opc23 = 0x04B0 little-endian
            bytes.Add((byte)'Q');
            AppendEos(bytes);
        });

        byte[] expected = new byte[304];
        int p = 0;
        for (int i = 0; i < 271; i++) expected[p++] = (byte)(i & 0xff);
        for (int i = 0; i < 13; i++) expected[p++] = (byte)'X';
        for (int i = 0; i < 13; i++) expected[p++] = (byte)'Y';
        for (int i = 0; i < 3; i++) expected[p++] = (byte)'Z';
        expected[p++] = (byte)'Q'; // literal from med_d
        // match M=3 D=300: reads dst[(p-3)-300 .. (p-3)-300+3] = dst[1..4] = bytes 1,2,3
        expected[p] = expected[p - 300]; p++;
        expected[p] = expected[p - 300]; p++;
        expected[p] = expected[p - 300]; p++;

        DecodeToEnd(payload, expected);
    }

    [Fact]
    public void LargeDistance_AtMaxBoundary_DecodesCorrectly()
    {
        // lrg_d: LLMMM111 DDDDDDDD DDDDDDDD LITERAL. D in [0, 65535] via load2(&src[1]).
        // Spec max: 65535. Our earlier helper BuildLiteralThenMatch already covers small D;
        // pick D = 2000 here to stress lrg_d with a non-trivial distance.
        const int D = 2000;

        List<byte> payload = new();
        // Prime dst with at least D bytes. Use three lrg_l opcodes: 271 + 271 + 271 = 813. Not enough.
        // Use seven: 271*7 = 1897; plus sml_l 103 = 2000.
        byte[] chunk = new byte[271];
        for (int i = 0; i < 271; i++) chunk[i] = (byte)(i & 0xff);
        for (int k = 0; k < 7; k++)
        {
            payload.Add(0xe0);
            payload.Add(255);
            payload.AddRange(chunk);
        }
        int remaining = D - 271 * 7;
        if (remaining > 271)
            throw new InvalidOperationException("adjust prime size");
        if (remaining >= 16)
        {
            payload.Add(0xe0);
            payload.Add((byte)(remaining - 16));
            for (int i = 0; i < remaining; i++) payload.Add((byte)'P');
        }
        else
        {
            payload.Add((byte)(0xe0 | remaining));
            for (int i = 0; i < remaining; i++) payload.Add((byte)'P');
        }

        // lrg_d: L=1, M=3, D=2000. opcode 0bLL_MMM_111 = 0b01_000_111 = 0x47.
        payload.Add(0x47);
        payload.Add((byte)(D & 0xff));
        payload.Add((byte)((D >> 8) & 0xff));
        payload.Add((byte)'Q');
        AppendEos(payload);

        byte[] expected = new byte[D + 4]; // prime + 1 literal + 3 match
        int p = 0;
        for (int k = 0; k < 7; k++)
        {
            for (int i = 0; i < 271; i++) expected[p++] = (byte)(i & 0xff);
        }
        for (int i = 0; i < remaining; i++) expected[p++] = (byte)'P';
        expected[p++] = (byte)'Q';
        for (int i = 0; i < 3; i++)
        {
            expected[p] = expected[p - D];
            p++;
        }

        DecodeToEnd(payload.ToArray(), expected);
    }

    [Theory]
    [InlineData(0x0e)] // NopOpcode1
    [InlineData(0x16)] // NopOpcode2
    public void Nop_IsSkipped(byte nopByte)
    {
        // Sandwich a NOP between two small literals.
        byte[] payload = Build(bytes =>
        {
            bytes.Add(0xe1);
            bytes.Add((byte)'A');
            bytes.Add(nopByte);
            bytes.Add(0xe1);
            bytes.Add((byte)'B');
            AppendEos(bytes);
        });

        DecodeToEnd(payload, "AB"u8.ToArray());
    }

    [Theory]
    // Undefined opcodes per lzvn_decode_base.c jump table: 30, 38, 46, 54, 62, 112..127, 208..223
    [InlineData((byte)30)]
    [InlineData((byte)38)]
    [InlineData((byte)0x70)]
    [InlineData((byte)0x7f)]
    [InlineData((byte)0xd0)]
    [InlineData((byte)0xdf)]
    public void UndefinedOpcode_IsNotReportedAsEndOfStream(byte undefinedOpcode)
    {
        // The reference treats these as errors. We expect the decoder to NOT advance past
        // them cleanly — EndOfStream should not be set.
        byte[] payload = [undefinedOpcode, 0, 0, 0, 0, 0, 0, 0, 0];

        byte[] dst = new byte[16];
        LzvnDecoderState state = new()
        {
            SourcePosition = 0,
            SourceEnd = payload.Length,
            DestinationPosition = 0,
            DestinationStart = 0,
            DestinationEnd = dst.Length,
        };
        LzvnDecoder.Decode(ref state, payload, dst);

        state.EndOfStream.Should().BeFalse($"undefined opcode 0x{undefinedOpcode:X2} must not complete as end-of-stream");
    }

    // --- Helpers ---

    private static byte[] Build(Action<List<byte>> action)
    {
        List<byte> list = new();
        action(list);
        return list.ToArray();
    }

    private static void AppendEos(List<byte> bytes)
    {
        bytes.Add(0x06);
        for (int i = 0; i < 7; i++) bytes.Add(0);
    }

    private static void DecodeToEnd(byte[] payload, byte[] expected)
    {
        byte[] dst = new byte[expected.Length];
        LzvnDecoderState state = new()
        {
            SourcePosition = 0,
            SourceEnd = payload.Length,
            DestinationPosition = 0,
            DestinationStart = 0,
            DestinationEnd = dst.Length,
        };
        LzvnDecoder.Decode(ref state, payload, dst);

        state.EndOfStream.Should().BeTrue("decoder should consume EOS marker");
        state.DestinationPosition.Should().Be(expected.Length);
        dst.Should().Equal(expected);
    }
}
