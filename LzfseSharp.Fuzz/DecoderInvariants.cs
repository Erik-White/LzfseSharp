using System;
using System.IO;

namespace LzfseSharp.Fuzz;

/// <summary>
/// The decoder's public contract, as enforced by the fuzz harness: for any input,
/// <see cref="LzfseDecoder.Decompress(Stream, Stream)"/> must either succeed or fail
/// only with one of its documented exception types
/// (<see cref="InvalidDataException"/>, <see cref="EndOfStreamException"/>). Any other
/// exception — or a non-deterministic successful decode — is reported by throwing
/// <see cref="InvariantException"/>, which the smoke runner reports or the fuzzer
/// registers as a crash.
/// </summary>
internal static class DecoderInvariants
{
    public static void Check(byte[] input)
    {
        byte[]? output = TryDecode(input, out bool succeeded);

        // A successful decode should be deterministic: re-decoding the same input must
        // produce the same bytes. Catches hypothetical state leaks across calls.
        if (succeeded)
        {
            byte[]? again = TryDecode(input, out bool succeededAgain);
            if (!succeededAgain)
                throw new InvariantException("non-deterministic decode: first decode succeeded, second failed");
            if (!output.AsSpan().SequenceEqual(again))
                throw new InvariantException("non-deterministic decode: output differs between identical inputs");
        }
    }

    private static byte[]? TryDecode(byte[] input, out bool succeeded)
    {
        using MemoryStream source = new(input, writable: false);
        using MemoryStream destination = new();

        try
        {
            LzfseDecoder.Decompress(source, destination);
        }
        catch (InvalidDataException)
        {
            succeeded = false;
            return null;
        }
        catch (EndOfStreamException)
        {
            succeeded = false;
            return null;
        }
        catch (Exception ex)
        {
            // Any exception type other than the two documented failure modes is a bug.
            throw new InvariantException($"Decompress threw undocumented {ex.GetType().Name}: {ex.Message}", ex);
        }

        succeeded = true;
        return destination.ToArray();
    }
}

internal sealed class InvariantException : Exception
{
    public InvariantException(string message) : base(message) { }
    public InvariantException(string message, Exception inner) : base(message, inner) { }
}
