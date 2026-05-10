using System.IO;
using SharpFuzz;

namespace LzfseSharp.Fuzz;

/// <summary>
/// Entry points for external fuzzers (libFuzzer and AFL++). Both require the
/// <c>LzfseSharp.dll</c> assembly to be instrumented beforehand via the
/// <c>sharpfuzz</c> CLI — see README.md.
/// </summary>
internal static class FuzzerEntryPoints
{
    // A generous upper bound on the decompressed size we try per fuzz input.
    // Larger values waste memory without exercising more decoder paths; smaller
    // values cause many valid inputs to report DestinationFull and mask bugs
    // that only manifest after a long decode.
    private const int DestinationBufferSize = 65_536;

    public static int RunLibFuzzer()
    {
        Fuzzer.LibFuzzer.Run(input =>
        {
            byte[] dst = new byte[DestinationBufferSize];
            DecoderInvariants.Check(dst, input.ToArray());
        });
        return 0;
    }

    public static int RunAfl()
    {
        Fuzzer.OutOfProcess.Run(stream =>
        {
            using MemoryStream ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] dst = new byte[DestinationBufferSize];
            DecoderInvariants.Check(dst, ms.ToArray());
        });
        return 0;
    }
}
