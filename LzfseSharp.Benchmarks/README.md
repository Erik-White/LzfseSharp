# LzfseSharp Benchmarks

This project contains BenchmarkDotNet benchmarks comparing the performance of LzfseSharp (C# implementation) against lzfse-net (native wrapper).

## Running the Benchmarks

```bash
dotnet run -c Release --project LzfseSharp.Benchmarks
```

## What's Being Measured

The benchmarks compare decompression performance between:
- **LzfseSharp_Decompress**: Pure C# implementation from this library
- **LzfseNet_Decompress**: Native C library via lzfse-net wrapper
