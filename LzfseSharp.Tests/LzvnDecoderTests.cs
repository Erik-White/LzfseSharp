using AwesomeAssertions;
using LzfseSharp.Lzvn;
using Xunit;

namespace LzfseSharp.Tests;

/// <summary>
/// Tests that drive <see cref="LzvnDecoder.Decode"/> directly (below the outer
/// <see cref="LzfseDecoder"/> framing) to exercise paths that the public API either
/// currently hides or can't trigger with valid reference-encoder output.
/// </summary>
public class LzvnDecoderTests
{
    [Fact]
    public void Decode_ResumePendingLiteral_DoesNotReconsumeNextOpcode()
    {
        // Scenario:
        //   * Simulate state after a prior call that stopped with 4 unwritten literal bytes.
        //   * Place the 4 literal bytes at the start of src (that's where sourcePosition points).
        //   * Place an LZVN EOS opcode (0x06 + 7 padding bytes) immediately after.
        //   * If Decode re-parses src[0] as an "opcode" and advances over the literal it would
        //     misinterpret 'Z' (0x5a) as a small-distance opcode and read junk.
        //   * With the fix it simply copies 4 bytes of literal then sees the EOS at src[4].

        byte[] src = new byte[4 + 8];
        src[0] = (byte)'W';
        src[1] = (byte)'X';
        src[2] = (byte)'Y';
        src[3] = (byte)'Z';
        src[4] = 0x06; // EOS opcode
        // src[5..12] = 0 padding for EOS marker

        byte[] dst = new byte[4];

        LzvnDecoderState state = new LzvnDecoderState
        {
            SourcePosition = 0,
            SourceEnd = src.Length,
            DestinationPosition = 0,
            DestinationStart = 0,
            DestinationEnd = dst.Length,
            LiteralLength = 4,
            MatchLength = 0,
            MatchDistance = 0,
        };

        LzvnDecoder.Decode(ref state, src, dst);

        state.EndOfStream.Should().BeTrue("EOS opcode should have been consumed after the pending literal");
        state.DestinationPosition.Should().Be(4);
        dst.Should().Equal((byte)'W', (byte)'X', (byte)'Y', (byte)'Z');
        state.SourcePosition.Should().Be(src.Length, "should consume the pending literal (4) plus the 8-byte EOS marker");
    }

    [Fact]
    public void Decode_ResumePendingMatch_DoesNotReconsumeNextOpcode()
    {
        // Prime destination with 4 bytes, then simulate a prior call that stopped with
        // a pending match (M=4, D=4). The next opcode in src is an EOS. A buggy resume
        // would treat src[0] (the EOS byte 0x06) as a new opcode BEFORE the match copy,
        // consuming it when it shouldn't be touched until the match completes.

        byte[] src = new byte[8];
        src[0] = 0x06; // EOS opcode
        // src[1..8] = 0 padding for EOS marker

        byte[] dst = new byte[8];
        dst[0] = (byte)'A';
        dst[1] = (byte)'B';
        dst[2] = (byte)'C';
        dst[3] = (byte)'D';

        LzvnDecoderState state = new LzvnDecoderState
        {
            SourcePosition = 0,
            SourceEnd = src.Length,
            DestinationPosition = 4,
            DestinationStart = 0,
            DestinationEnd = dst.Length,
            LiteralLength = 0,
            MatchLength = 4,
            MatchDistance = 4,
        };

        LzvnDecoder.Decode(ref state, src, dst);

        state.EndOfStream.Should().BeTrue("EOS opcode should have been consumed after the pending match");
        state.DestinationPosition.Should().Be(8);
        dst.Should().Equal((byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'A', (byte)'B', (byte)'C', (byte)'D');
        state.SourcePosition.Should().Be(src.Length);
    }

    [Fact]
    public void Decode_PartialMatchResume_ProducesCorrectOutput()
    {
        // Build a payload that produces: 4-byte literal "ABCD" then a match of M=6, D=4.
        // Split the decode at destinationEnd=7 (after 4 literal + 3 of the 6 match bytes).
        // On resume we expect the remaining 3 bytes of the match to be copied — this
        // exercises both bug #8 (framing fields must survive) and bug #9 (M must be the
        // OUTSTANDING bytes, not the original 6).
        //
        // Opcode: small distance "LLMMMDDD" style. Using the 2-byte small-distance encoding:
        //   top 2 bits = L (2 bits, range 0..3)
        //   bits 3..5  = M - 3 (3 bits, range 3..10)
        //   bits 0..2  = D high 3 bits
        //   next byte  = D low 8 bits
        // For L=4 we need to use a preceding large-literal opcode, so it's simpler to
        // construct with a large-literal (0xe0) + len byte + 4 literal bytes, then a
        // small-distance opcode with L=0, M=6, D=4.

        byte[] payload = BuildLiteralThenMatch(literal: "ABCD"u8.ToArray(), matchLen: 6, matchDist: 4);

        byte[] expected = new byte[10];
        "ABCD"u8.CopyTo(expected);
        // match copies bytes 0..5 from positions (4-4)..(4+6-4-1) = 0..5 into 4..9
        for (int i = 0; i < 6; i++)
            expected[4 + i] = expected[i];

        byte[] fullDst = new byte[expected.Length];
        RunLzvnDecoder(payload, fullDst, fullDst.Length);
        fullDst.Should().Equal(expected, "sanity check: one-shot decode must match the expected output");

        // Now run in two parts: stop at destinationLength=7, then finish.
        byte[] splitDst = new byte[expected.Length];
        LzvnDecoderState state = new LzvnDecoderState
        {
            SourcePosition = 0,
            SourceEnd = payload.Length,
            DestinationPosition = 0,
            DestinationStart = 0,
            DestinationEnd = 7,
        };
        LzvnDecoder.Decode(ref state, payload, splitDst);
        state.DestinationPosition.Should().Be(7);
        state.MatchLength.Should().Be((nuint)3, "3 of 6 match bytes remain outstanding after the first call");

        // Resume — the caller only needs to extend DestinationEnd, since the fix preserves
        // all other framing fields across the dst-full return.
        state.DestinationEnd = expected.Length;
        LzvnDecoder.Decode(ref state, payload, splitDst);

        state.DestinationPosition.Should().Be(expected.Length);
        state.EndOfStream.Should().BeTrue();
        splitDst.Should().Equal(expected, "resumed decode must produce the same output as a one-shot decode");
    }

    [Fact]
    public void Decode_PartialLiteralResume_ProducesCorrectOutput()
    {
        // Large-literal (16 bytes) split mid-copy. Exercises CopyLiteralBytes' resume path.
        byte[] literal = "0123456789ABCDEF"u8.ToArray();
        byte[] payload = new byte[2 + literal.Length + 8];
        payload[0] = 0xe0;                      // large literal
        payload[1] = (byte)(literal.Length - LzfseSharp.Lzvn.LzvnConstants.LargeLiteralBias);
        literal.CopyTo(payload, 2);
        payload[2 + literal.Length] = 0x06;     // EOS
        // trailing 7 zeros already present from array init

        byte[] splitDst = new byte[literal.Length];
        LzvnDecoderState state = new LzvnDecoderState
        {
            SourcePosition = 0,
            SourceEnd = payload.Length,
            DestinationPosition = 0,
            DestinationStart = 0,
            DestinationEnd = 10,
        };
        LzvnDecoder.Decode(ref state, payload, splitDst);
        state.DestinationPosition.Should().Be(10);
        state.LiteralLength.Should().Be((nuint)(literal.Length - 10), "6 of 16 literal bytes remain outstanding");

        state.DestinationEnd = literal.Length;
        LzvnDecoder.Decode(ref state, payload, splitDst);

        state.DestinationPosition.Should().Be(literal.Length);
        state.EndOfStream.Should().BeTrue();
        splitDst.Should().Equal(literal);
    }

    private static byte[] BuildLiteralThenMatch(byte[] literal, int matchLen, int matchDist)
    {
        // Small literal opcode: 0xe0 | L with L in 1..15 (0 is unused, 0xe0 is large literal).
        // Then a "large distance" opcode byte (0bLLMMM111) + 2-byte little-endian D.
        //   L=0 (no attached literal), M = encodedM + 3, where encodedM is bits 3..5.
        if (literal.Length < 1 || literal.Length > 15)
            throw new ArgumentException($"literal length {literal.Length} not encodable in small-literal opcode");

        int smallLiteralOpcodeLen = 1;
        int largeDistanceOpcodeLen = 3;
        byte[] payload = new byte[smallLiteralOpcodeLen + literal.Length + largeDistanceOpcodeLen + 8];
        int p = 0;

        payload[p++] = (byte)(0xe0 | literal.Length);
        literal.CopyTo(payload, p);
        p += literal.Length;

        int encodedM = matchLen - LzfseSharp.Lzvn.LzvnConstants.MatchLengthBias;
        if (encodedM < 0 || encodedM > 7)
            throw new ArgumentException($"matchLen {matchLen} not encodable in single large-distance opcode");
        payload[p++] = (byte)((0 << 6) | (encodedM << 3) | 0x07);
        payload[p++] = (byte)(matchDist & 0xff);
        payload[p++] = (byte)((matchDist >> 8) & 0xff);

        payload[p++] = 0x06; // EOS
        // remaining 7 bytes are already zero

        return payload;
    }

    [Fact]
    public void Decompress_LzvnBlockWithUnderSizedPayload_DoesNotCrash()
    {
        byte[] input =
        [
            0x62, 0x76, 0x78, 0x6E,                          // bvxn magic
            0x01, 0x00, 0x10, 0x00,                          // n_raw_bytes = 0x100001
            0x0A, 0x00, 0x00, 0x00,                          // n_payload_bytes = 10
            0xE0, 0x48,                                      // large-literal, L = 72 + 16 = 88
            0x06, 0, 0, 0, 0, 0, 0, 0,                       // EOS marker
            0x62, 0x76, 0x78, 0x24,                          // bvx$
        ];

        byte[] dst = new byte[37];
        LzfseDecoder.Decompress(dst, input, out DecompressStatus status);

        status.Should().NotBe(DecompressStatus.Ok);
    }

    [Theory]
    [InlineData(1)] // just the 0x06 byte
    [InlineData(4)] // 0x06 + partial padding
    [InlineData(7)] // one byte short of full marker
    public void Decode_TruncatedEosMarker_DoesNotReportEndOfStream(int srcLen)
    {
        // The LZVN EOS marker is the byte 0x06 followed by 7 padding bytes. If the
        // decoder accepts 0x06 without verifying the full 8-byte marker, a crafted
        // stream can make SourcePosition advance past SourceEnd and report success.
        byte[] src = new byte[srcLen];
        src[0] = 0x06;

        byte[] dst = new byte[16];
        LzvnDecoderState state = new LzvnDecoderState
        {
            SourcePosition = 0,
            SourceEnd = src.Length,
            DestinationPosition = 0,
            DestinationStart = 0,
            DestinationEnd = dst.Length,
        };

        LzvnDecoder.Decode(ref state, src, dst);

        state.EndOfStream.Should().BeFalse("a truncated EOS marker must not be treated as end of stream");
        state.SourcePosition.Should().BeLessThanOrEqualTo(src.Length, "decoder must not advance past the end of the source");
    }

    private static void RunLzvnDecoder(byte[] payload, byte[] dst, int destinationEnd)
    {
        LzvnDecoderState state = new()
        {
            SourcePosition = 0,
            SourceEnd = payload.Length,
            DestinationPosition = 0,
            DestinationStart = 0,
            DestinationEnd = destinationEnd,
        };
        LzvnDecoder.Decode(ref state, payload, dst);
    }
}
