using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Lzfse;

namespace LzfseSharp.Benchmarks;

/// <summary>
/// Benchmarks that stress the LZVN literal copy path. LZVN is only picked by the
/// reference encoder for small inputs (roughly ≤ 1KB) with moderate compressibility,
/// so we construct text-with-noise payloads in that size range and decompress them
/// in a throughput loop. This isolates the benefit of the wide literal fast path in
/// <c>CopyLiteralBytes</c> against the byte-by-byte fallback.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 10)]
public class LiteralHeavyBenchmarks
{
    private byte[] _compressedData = [];
    private byte[] _decompressedBuffer = [];
    private int _expectedDecompressedSize;

    [Params(256, 1024)]
    public int DataSizeBytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        byte[] uncompressedData = GenerateLzvnFavourableData(DataSizeBytes);

        byte[] compressedBuffer = new byte[uncompressedData.Length * 2 + 1024];
        int compressedSize = LzfseCompressor.Compress(uncompressedData, compressedBuffer);
        _compressedData = new byte[compressedSize];
        Array.Copy(compressedBuffer, _compressedData, compressedSize);
        _expectedDecompressedSize = uncompressedData.Length;

        uint firstMagic = BitConverter.ToUInt32(_compressedData, 0);
        if (firstMagic != 0x6e787662)
            throw new InvalidOperationException(
                $"Test data did not produce an LZVN block (first magic 0x{firstMagic:X8}). " +
                $"Size {DataSizeBytes} may need adjusting.");

        _decompressedBuffer = new byte[_expectedDecompressedSize];

        int bytesWritten = LzfseDecoder.Decompress(_decompressedBuffer, _compressedData);
        if (bytesWritten != _expectedDecompressedSize)
            throw new InvalidOperationException(
                $"LzfseSharp decompression failed: expected {_expectedDecompressedSize} bytes, got {bytesWritten}");
    }

    private static byte[] GenerateLzvnFavourableData(int size)
    {
        // Text-like content with a dictionary of short tokens, plus 10% positional noise.
        // At <= 1KB this reliably produces a bvxn block in the reference encoder.
        const string words = "the quick brown fox jumps over the lazy dog and runs into woods darkness ";
        byte[] data = new byte[size];
        for (int i = 0; i < size; i++)
            data[i] = (byte)words[i % words.Length];
        Random random = new(42);
        for (int i = 0; i < size / 10; i++)
            data[random.Next(size)] = (byte)random.Next(256);
        return data;
    }

    [Benchmark(Baseline = true)]
    public int LzfseSharp_Decompress()
    {
        Array.Clear(_decompressedBuffer);
        return LzfseDecoder.Decompress(_decompressedBuffer, _compressedData);
    }

    [Benchmark]
    public int LzfseNet_Decompress()
    {
        Array.Clear(_decompressedBuffer);
        return LzfseCompressor.Decompress(_compressedData, _decompressedBuffer);
    }
}
