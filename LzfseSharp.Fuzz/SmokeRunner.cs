using System;

namespace LzfseSharp.Fuzz;

/// <summary>
/// Deterministic in-process fuzz. Generates a fixed number of inputs from a
/// fixed seed, asserts the decoder is total. Requires no external tooling;
/// suitable for CI and local verification.
/// </summary>
internal static class SmokeRunner
{
    private const int Iterations = 10_000;

    // Fixed seed for reproducibility. If a smoke run fails, the seed + iteration
    // number from the output uniquely identifies the offending input.
    private const int Seed = 0x4C5A4653; // "LZFS" as ASCII

    private const int MaxFailuresBeforeAbort = 10;

    public static int Run()
    {
        Random rng = new Random(Seed);
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
