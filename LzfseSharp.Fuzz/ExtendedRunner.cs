using System;
using System.Diagnostics;

namespace LzfseSharp.Fuzz;

/// <summary>
/// Long-running in-process fuzzer. Runs a fixed iteration budget per seed over a
/// list of seeds. Intended for manual / scheduled runs, not every-commit CI.
///
/// Shares the <see cref="InputGenerator"/> and <see cref="DecoderInvariants"/>
/// used by <see cref="SmokeRunner"/>, so coverage is identical — just more of it.
/// </summary>
internal static class ExtendedRunner
{
    private const int DefaultIterationsPerSeed = 5_000_000;
    private const int MaxFailuresBeforeAbort = 10;

    public static int Run(int[] seeds, int iterationsPerSeed = DefaultIterationsPerSeed)
    {
        if (seeds.Length == 0)
        {
            Console.Error.WriteLine("extended mode requires at least one seed");
            return 2;
        }

        Console.WriteLine($"Extended run: {seeds.Length} seed(s) × {iterationsPerSeed:N0} iterations each = {(long)seeds.Length * iterationsPerSeed:N0} total");

        int totalFailures = 0;
        Stopwatch overall = Stopwatch.StartNew();

        foreach (int seed in seeds)
        {
            int failures = RunOneSeed(seed, iterationsPerSeed);
            totalFailures += failures;
            if (totalFailures >= MaxFailuresBeforeAbort)
            {
                Console.Error.WriteLine($"Aborting after {totalFailures} failures.");
                return 1;
            }
        }

        overall.Stop();
        if (totalFailures > 0)
        {
            Console.Error.WriteLine($"FAIL: {totalFailures} invariant violation(s) across {seeds.Length} seed(s) in {overall.Elapsed.TotalMinutes:F1} minutes.");
            return 1;
        }

        Console.WriteLine($"OK: {seeds.Length} seed(s) clean in {overall.Elapsed.TotalMinutes:F1} minutes.");
        return 0;
    }

    private static int RunOneSeed(int seed, int iterations)
    {
        Random rng = new Random(seed);
        int failures = 0;
        Stopwatch sw = Stopwatch.StartNew();

        // Report progress every 500k iterations to give operators visual feedback
        // on long runs (each row is ~10-20 seconds of work in Release).
        const int ReportEvery = 500_000;

        for (int i = 0; i < iterations; i++)
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
                ReportFailure(seed, i, input, dst.Length, ex);
                if (failures >= MaxFailuresBeforeAbort)
                    break;
            }

            if ((i + 1) % ReportEvery == 0)
            {
                double rate = (i + 1) / sw.Elapsed.TotalSeconds;
                Console.WriteLine($"  [seed {seed}] {i + 1:N0}/{iterations:N0} ({rate:N0} iter/s, elapsed {sw.Elapsed.TotalSeconds:F0}s)");
            }
        }

        sw.Stop();
        if (failures == 0)
            Console.WriteLine($"Seed {seed}: clean ({iterations:N0} iterations in {sw.Elapsed.TotalSeconds:F1}s, {iterations / sw.Elapsed.TotalSeconds:N0} iter/s)");
        return failures;
    }

    private static void ReportFailure(int seed, int iteration, byte[] input, int dstSize, Exception ex)
    {
        Console.Error.WriteLine($"[seed {seed} iter {iteration}] invariant violated: {ex.GetType().Name}: {ex.Message}");
        int previewLength = Math.Min(64, input.Length);
        string preview = BitConverter.ToString(input, 0, previewLength);
        string ellipsis = input.Length > previewLength ? "..." : "";
        Console.Error.WriteLine($"  input ({input.Length} bytes): {preview}{ellipsis}");
        Console.Error.WriteLine($"  dst size: {dstSize}");
    }
}
