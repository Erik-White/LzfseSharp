using BenchmarkDotNet.Running;

namespace LzfseSharp.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        BenchmarkRunner.Run<DecompressionBenchmarks>(args: args);
    }
}
