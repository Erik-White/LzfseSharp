using System;

namespace LzfseSharp.Fuzz;

/// <summary>
/// The decoder's public contract, as enforced by the fuzz harness: for any input,
/// <see cref="LzfseDecoder.Decompress(Span{byte}, ReadOnlySpan{byte}, out DecompressStatus)"/>
/// must return normally with a defined status and a byte count in
/// <c>[0, dst.Length]</c>, and a successful decode must be deterministic. Any
/// violation is reported by throwing <see cref="InvariantException"/>, which
/// either the smoke runner reports or the fuzzer registers as a crash.
/// </summary>
internal static class DecoderInvariants
{
    public static void Check(byte[] dst, byte[] input)
    {
        int written;
        DecompressStatus status;

        try
        {
            written = LzfseDecoder.Decompress(dst, input, out status);
        }
        catch (Exception ex)
        {
            // The out-param overload must never throw. The legacy overload may throw
            // ArgumentException on dst-full, but the out-param overload is documented
            // as total.
            throw new InvariantException($"Decompress threw {ex.GetType().Name}: {ex.Message}", ex);
        }

        if (!Enum.IsDefined(status))
            throw new InvariantException($"status out of range: {(int)status}");

        if (written < 0 || written > dst.Length)
            throw new InvariantException($"written ({written}) not in [0, {dst.Length}]");

        // A successful decode should be deterministic: re-decoding the same input
        // must produce the same bytes. Catches hypothetical state leaks across calls.
        if (status == DecompressStatus.Ok)
            VerifyDeterministicDecode(dst, input, written);
    }

    private static void VerifyDeterministicDecode(byte[] firstOutput, byte[] input, int expectedWritten)
    {
        byte[] second = new byte[firstOutput.Length];
        int writtenAgain = LzfseDecoder.Decompress(second, input, out DecompressStatus statusAgain);

        if (statusAgain != DecompressStatus.Ok || writtenAgain != expectedWritten)
            throw new InvariantException($"non-deterministic Ok decode: {statusAgain}/{writtenAgain} vs Ok/{expectedWritten}");

        if (!firstOutput.AsSpan(0, expectedWritten).SequenceEqual(second.AsSpan(0, writtenAgain)))
            throw new InvariantException("non-deterministic Ok decode: output differs");
    }
}

internal sealed class InvariantException : Exception
{
    public InvariantException(string message) : base(message) { }
    public InvariantException(string message, Exception inner) : base(message, inner) { }
}
