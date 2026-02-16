using BenchmarkDotNet.Running;

namespace LzfseSharp.Benchmarks;

class Program
{
    protected Program()
    {
    }

    static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
