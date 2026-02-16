# LzfseSharp Benchmarks

This project contains BenchmarkDotNet benchmarks comparing the performance of LzfseSharp (C# implementation) against lzfse-net (native wrapper).

## Running the Benchmarks

### Interactive Mode

Run without arguments to see an interactive menu:

```bash
dotnet run -c Release --project LzfseSharp.Benchmarks --framework net10.0
```

### Run Specific Benchmark

```bash
dotnet run -c Release --project LzfseSharp.Benchmarks --framework net10.0 --filter *DecompressionBenchmarks*
```

## What's Being Measured

The benchmarks compare decompression performance between:
- **LzfseSharp_Decompress**: Pure C# implementation from this library
- **LzfseNet_Decompress**: Native C library via lzfse-net wrapper
