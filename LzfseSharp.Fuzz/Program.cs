using System;

namespace LzfseSharp.Fuzz;

/// <summary>
/// Fuzz harness for <see cref="LzfseDecoder"/>. CLI dispatch only; the actual
/// work lives in <see cref="DecoderInvariants"/> (contract), <see cref="InputGenerator"/>
/// (inputs), <see cref="SmokeRunner"/> (in-process mode), and
/// <see cref="FuzzerEntryPoints"/> (libFuzzer / AFL++ modes).
///
/// Modes:
///   (no args) / --smoke : 10,000 fixed-seed iterations, exits non-zero on invariant
///                         violation. Runs without external tooling; suitable for CI.
///   --libfuzzer         : SharpFuzz libFuzzer entry point. Requires the compiled DLL
///                         to be instrumented first — see README.md.
///   --afl               : SharpFuzz out-of-process AFL++ entry point. Same
///                         instrumentation requirement — see README.md.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "--smoke";
        return mode switch
        {
            "--libfuzzer" => FuzzerEntryPoints.RunLibFuzzer(),
            "--afl" => FuzzerEntryPoints.RunAfl(),
            "--smoke" or "" => SmokeRunner.Run(),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: LzfseSharp.Fuzz [--smoke | --libfuzzer | --afl]");
        return 2;
    }
}
