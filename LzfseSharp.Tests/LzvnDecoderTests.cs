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
}
