using System;

namespace LzfseSharp.Fuzz;

/// <summary>
/// Fuzz harness for <see cref="LzfseDecoder"/>. CLI dispatch only; the actual
/// work lives in <see cref="DecoderInvariants"/> (contract), <see cref="InputGenerator"/>
/// (inputs), <see cref="SmokeRunner"/> (in-process mode), and
/// <see cref="FuzzerEntryPoints"/> (libFuzzer / AFL++ modes).
///
/// Modes:
///   (no args) / --smoke       : 10,000 iterations with a varying seed (logged on
///                               start-up). Exits non-zero on invariant violation.
///                               Runs without external tooling; suitable for CI.
///   --smoke --seed &lt;int&gt; : Same, but with a fixed seed to reproduce a prior run.
///   --libfuzzer               : SharpFuzz libFuzzer entry point. Requires
///                               instrumentation first — see README.md.
///   --afl                     : SharpFuzz out-of-process AFL++ entry point. Same
///                               instrumentation requirement — see README.md.
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
            "--smoke" or "" => SmokeRunner.Run(ParseOptionalSeed(args)),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Parses <c>--seed &lt;int&gt;</c> from the remaining args. Returns null when
    /// absent (caller picks its own seed) or on malformed input (caller surfaces
    /// a usage message).
    /// </summary>
    private static int? ParseOptionalSeed(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--seed" && int.TryParse(args[i + 1], out int seed))
                return seed;
        }
        return null;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: LzfseSharp.Fuzz [--smoke [--seed <int>] | --libfuzzer | --afl]");
        return 2;
    }
}
