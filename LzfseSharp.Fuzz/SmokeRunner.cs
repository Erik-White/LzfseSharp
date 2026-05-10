using System;

namespace LzfseSharp.Fuzz;

/// <summary>
/// In-process fuzz runner. Generates a fixed number of inputs from a seed that
/// the caller may override, and asserts the decoder is total. Requires no
/// external tooling.
///
/// The seed defaults to a time-derived value so repeated CI runs explore
/// different inputs; the actual seed is always logged so failures can be
/// reproduced locally via <c>--seed &lt;value&gt;</c>.
/// </summary>
internal static class SmokeRunner
{
    private const int Iterations = 10_000;
    private const int MaxFailuresBeforeAbort = 10;

    public static int Run(int? seed = null)
    {
        int effectiveSeed = seed ?? Random.Shared.Next();
        Console.WriteLine($"Smoke run: seed={effectiveSeed} iterations={Iterations}");

        Random rng = new Random(effectiveSeed);
        int failures = 0;

        for (int i = 0; i < Iterations; i++)
        {
            byte[] input = InputGenerator.Generate(rng);
            byte[] dst = new byte[rng.Next(1, 4096)];

            try
            {
                DecoderInvariants.Check(dst, input);
            }
            catch (Exception ex)
            {
                failures++;
                ReportFailure(i, input, dst.Length, ex);
                if (failures >= MaxFailuresBeforeAbort)
                {
                    Console.Error.WriteLine("Too many failures, aborting.");
                    return 1;
                }
            }
        }

        if (failures > 0)
        {
            Console.Error.WriteLine($"FAIL: {failures} invariant violation(s) across {Iterations} iterations.");
            return 1;
        }

        Console.WriteLine($"OK: {Iterations} iterations, no invariant violations.");
        return 0;
    }

    private static void ReportFailure(int iteration, byte[] input, int dstSize, Exception ex)
    {
        Console.Error.WriteLine($"[iter {iteration}] invariant violated: {ex.GetType().Name}: {ex.Message}");

        int previewLength = Math.Min(32, input.Length);
        string preview = BitConverter.ToString(input, 0, previewLength);
        string ellipsis = input.Length > previewLength ? "..." : "";
        Console.Error.WriteLine($"  input ({input.Length} bytes): {preview}{ellipsis}");
        Console.Error.WriteLine($"  dst size: {dstSize}");
    }
}
