namespace LzfseSharp.Lzvn;

/// <summary>
/// LZVN decoder state
/// </summary>
internal struct LzvnDecoderState
{
    public int SourcePosition;
    public int SourceEnd;
    public int DestinationPosition;
    public int DestinationStart;
    public int DestinationEnd;
    public nuint LiteralLength;
    public nuint MatchLength;
    public nuint MatchDistance;
    public int PreviousDistance;
    public bool EndOfStream;

    /// <summary>
    /// Set to true when <see cref="LzvnDecoder.Decode"/> detects a malformed stream
    /// (invalid match distance, undefined opcode). Distinct from simply running out
    /// of source or destination, which leaves this false.
    /// </summary>
    public bool Malformed;
}
