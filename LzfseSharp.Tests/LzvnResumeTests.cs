using AwesomeAssertions;
using LzfseSharp.Lzvn;
using Xunit;

namespace LzfseSharp.Tests;

/// <summary>
/// Tests that exercise the LZVN decoder's partial-state / resume path.
/// Prior to the fix, <see cref="LzvnDecoder.Decode"/> assumed every entry into a
/// Process* helper had to skip over an opcode at <c>sourcePosition</c>. On a
/// resume (triggered by <c>StatusDstFull</c> on the previous call), the opcode
/// has already been consumed — re-advancing over it would eat part of the next
/// opcode's bytes and corrupt output.
/// </summary>
public class LzvnResumeTests
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
}
