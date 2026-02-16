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
}
