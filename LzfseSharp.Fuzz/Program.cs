using System;

namespace LzfseSharp.Fuzz;

/// <summary>
/// Fuzz harness for <see cref="LzfseDecoder"/>. CLI dispatch only; the actual
/// work lives in <see cref="DecoderInvariants"/> (contract), <see cref="InputGenerator"/>
/// (inputs), <see cref="SmokeRunner"/> / <see cref="ExtendedRunner"/>
/// (in-process modes), and <see cref="FuzzerEntryPoints"/> (libFuzzer / AFL++).
///
/// Modes:
///   (no args) / --smoke        : 10,000 iterations with a varying seed (logged on
///                                start-up). ~1s runtime. Suitable for CI.
///   --smoke --seed N           : Same, but fixed seed for reproducing a prior run.
///   --extended --seeds A,B,... : 5,000,000 iterations per comma-separated seed.
///                                Seeds can also be a range: --seeds 1-100.
///                                Manual / scheduled runs only.
///   --extended --seeds ... --iterations N : Same with a custom iteration count.
///   --libfuzzer                : SharpFuzz libFuzzer entry point. Requires
///                                instrumentation first — see README.md.
///   --afl                      : SharpFuzz out-of-process AFL++ entry point.
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
            "--extended" => RunExtended(args),
            "--smoke" or "" => SmokeRunner.Run(ParseOptionalSeed(args)),
            _ => Usage(),
        };
    }

    private static int RunExtended(string[] args)
    {
        int[]? seeds = ParseSeedList(args);
        if (seeds is null)
        {
            Console.Error.WriteLine("--extended requires --seeds <comma-separated ints>");
            return 2;
        }

        int iterations = ParseOptionalIterations(args) ?? 5_000_000;
        return ExtendedRunner.Run(seeds, iterations);
    }

    /// <summary>
    /// Parses <c>--seed &lt;int&gt;</c> from the remaining args. Returns null when
    /// absent or malformed.
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

    private static int? ParseOptionalIterations(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--iterations" && int.TryParse(args[i + 1], out int n) && n > 0)
                return n;
        }
        return null;
    }

    /// <summary>
    /// Parses <c>--seeds</c>. Accepts either a comma-separated list of integers
    /// (<c>1,2,5,9</c>) or an inclusive range (<c>1-600</c>). Returns null when
    /// absent or malformed.
    /// </summary>
    private static int[]? ParseSeedList(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--seeds") continue;
            string value = args[i + 1];

            // Range form: "<start>-<end>" inclusive.
            int dashIdx = value.IndexOf('-', startIndex: value.StartsWith('-') ? 1 : 0);
            if (dashIdx > 0 && !value.Contains(','))
            {
                if (int.TryParse(value.AsSpan(0, dashIdx), out int start)
                    && int.TryParse(value.AsSpan(dashIdx + 1), out int end)
                    && end >= start)
                {
                    int[] range = new int[end - start + 1];
                    for (int k = 0; k < range.Length; k++) range[k] = start + k;
                    return range;
                }
                return null;
            }

            // Comma-separated list form.
            string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int[] seeds = new int[parts.Length];
            for (int j = 0; j < parts.Length; j++)
            {
                if (!int.TryParse(parts[j], out seeds[j]))
                    return null;
            }
            return seeds.Length > 0 ? seeds : null;
        }
        return null;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: LzfseSharp.Fuzz [--smoke [--seed <int>] | --extended --seeds <a,b,...> [--iterations <n>] | --libfuzzer | --afl]");
        return 2;
    }
}
