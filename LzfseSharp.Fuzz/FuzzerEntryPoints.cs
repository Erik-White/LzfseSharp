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
    public static int RunLibFuzzer()
    {
        Fuzzer.LibFuzzer.Run(input =>
        {
            DecoderInvariants.Check(input.ToArray());
        });
        return 0;
    }

    public static int RunAfl()
    {
        Fuzzer.OutOfProcess.Run(stream =>
        {
            using MemoryStream ms = new MemoryStream();
            stream.CopyTo(ms);
            DecoderInvariants.Check(ms.ToArray());
        });
        return 0;
    }
}
