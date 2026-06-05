using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Lzfse;

namespace LzfseSharp.Benchmarks;

/// <summary>
/// Benchmarks comparing LzfseSharp C# implementation against lzfse-net native wrapper
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 10)]
public class DecompressionBenchmarks
{
    private byte[] _compressedData = Array.Empty<byte>();
    private byte[] _decompressedBuffer = Array.Empty<byte>();
    private MemoryStream _source = new();
    private MemoryStream _destination = new();
    private int _expectedDecompressedSize;

    [Params(1, 10, 100, 1000)]
    public int DataSizeKB { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Generate test data with some patterns (more realistic than random data)
        byte[] uncompressedData = GenerateTestData(DataSizeKB * 1024);

        // Compress using lzfse-net to get compressed data
        byte[] compressedBuffer = new byte[uncompressedData.Length * 2 + 1024];
        int compressedSize = LzfseCompressor.Compress(uncompressedData, compressedBuffer);
        _compressedData = new byte[compressedSize];
        Array.Copy(compressedBuffer, _compressedData, compressedSize);
        _expectedDecompressedSize = uncompressedData.Length;

        // Allocate the lzfse-net buffer and the reusable streams for the LzfseSharp path.
        _decompressedBuffer = new byte[_expectedDecompressedSize];
        _source = new MemoryStream(_compressedData, writable: false);
        _destination = new MemoryStream(_expectedDecompressedSize);

        // Verify both implementations can decompress
        VerifyDecompression();
    }

    private void VerifyDecompression()
    {
        // Verify LzfseSharp
        _source.Position = 0;
        _destination.SetLength(0);
        LzfseDecoder.Decompress(_source, _destination);
        if (_destination.Length != _expectedDecompressedSize)
        {
            throw new InvalidOperationException(
                $"LzfseSharp decompression failed: expected {_expectedDecompressedSize} bytes, got {_destination.Length}");
        }

        // Verify lzfse-net
        Array.Clear(_decompressedBuffer);
        int decompressedSize = LzfseCompressor.Decompress(_compressedData, _decompressedBuffer);
        if (decompressedSize != _expectedDecompressedSize)
        {
            throw new InvalidOperationException(
                $"lzfse-net decompression failed: expected {_expectedDecompressedSize} bytes, got {decompressedSize}");
        }
    }

    private static byte[] GenerateTestData(int size)
    {
        var data = new byte[size];
        var random = new Random(42); // Fixed seed for reproducibility

        // Generate data with patterns for better compression ratio
        for (int i = 0; i < size; i++)
        {
            // Mix of patterns: repeating sequences, text-like data, and some randomness
            if (i % 100 < 50)
            {
                // Repeating pattern
                data[i] = (byte)((i % 26) + 65); // A-Z
            }
            else if (i % 100 < 80)
            {
                // Text-like (ASCII printable characters)
                data[i] = (byte)(random.Next(95) + 32);
            }
            else
            {
                // Random bytes
                data[i] = (byte)random.Next(256);
            }
        }

        return data;
    }

    [Benchmark(Baseline = true)]
    public long LzfseSharp_Decompress()
    {
        _source.Position = 0;
        _destination.SetLength(0);
        LzfseDecoder.Decompress(_source, _destination);
        return _destination.Length;
    }

    [Benchmark]
    public int LzfseNet_Decompress()
    {
        Array.Clear(_decompressedBuffer);
        return LzfseCompressor.Decompress(_compressedData, _decompressedBuffer);
    }
}
