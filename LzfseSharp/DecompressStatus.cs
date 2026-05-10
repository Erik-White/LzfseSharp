namespace LzfseSharp;

/// <summary>
/// Outcome of a call to <see cref="LzfseDecoder.Decompress(Span{byte}, ReadOnlySpan{byte}, out DecompressStatus)"/>.
/// Mirrors the LZFSE_STATUS_* values from the reference implementation
/// (https://github.com/lzfse/lzfse, src/lzfse.h).
/// </summary>
public enum DecompressStatus
{
    /// <summary>
    /// The stream was decoded successfully and the end-of-stream marker was consumed.
    /// </summary>
    Ok,

    /// <summary>
    /// The source buffer ended before the decoder finished: a block header, payload,
    /// or the end-of-stream marker was missing. Equivalent to the reference's
    /// LZFSE_STATUS_SRC_EMPTY.
    /// </summary>
    SourceTruncated,

    /// <summary>
    /// The destination buffer was exhausted before the decoder finished. The stream
    /// itself may be well-formed — the caller just needs a larger buffer.
    /// Equivalent to the reference's LZFSE_STATUS_DST_FULL.
    /// </summary>
    DestinationFull,

    /// <summary>
    /// The stream is malformed: invalid magic, inconsistent header, bad frequency
    /// table, out-of-range match distance, etc. Equivalent to the reference's
    /// LZFSE_STATUS_ERROR.
    /// </summary>
    Malformed,
}
