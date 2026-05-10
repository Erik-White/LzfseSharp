# LzfseSharp

A C# port of Apple's [LZFSE](https://github.com/lzfse/lzfse) (Lempel-Ziv Finite State Entropy) compression algorithm. This library provides **decoding-only** functionality for decompressing LZFSE-compressed data.

## What is LZFSE?

LZFSE is a Lempel-Ziv style data compression algorithm using Finite State Entropy coding, introduced by Apple with OS X 10.11 and iOS 9. It targets similar compression ratios to DEFLATE but with significantly higher compression and decompression speeds.

## Features

- Supports all LZFSE block types:
  - LZFSE compressed blocks (V1 and V2 with FSE encoding)
  - LZVN compressed blocks (simpler algorithm for small data)
  - Uncompressed blocks

## Installation

```bash
dotnet add package LzfseSharp
```

## Usage

### Basic Decompression

```csharp
using LzfseSharp;

byte[] compressedData = File.ReadAllBytes("data.lzfse");

// Allocate a buffer at least as large as the expected uncompressed size.
// Over-allocating is fine — the decoder reports the actual bytes written.
byte[] decompressedData = new byte[expectedSize];

int bytesWritten = LzfseDecoder.Decompress(
    decompressedData,
    compressedData,
    out DecompressStatus status);

switch (status)
{
    case DecompressStatus.Ok:
        Console.WriteLine($"Decompressed {bytesWritten} bytes");
        break;
    case DecompressStatus.SourceTruncated:
        Console.WriteLine($"Input was truncated; {bytesWritten} bytes recovered");
        break;
    case DecompressStatus.DestinationFull:
        Console.WriteLine("Destination buffer was too small");
        break;
    case DecompressStatus.Malformed:
        Console.WriteLine("Input stream is not valid LZFSE");
        break;
}
```

There is also a simpler overload without the `out DecompressStatus` parameter
that returns the bytes written and throws `ArgumentException` when the
destination is too small. Prefer the `DecompressStatus` overload when you need
to distinguish truncated input from malformed input, or when partial output on
failure is useful.

### Working with Streams

```csharp
using LzfseSharp;

using var inputStream = File.OpenRead("data.lzfse");
using var outputStream = File.OpenWrite("data.bin");

byte[] compressed = new byte[inputStream.Length];
inputStream.ReadExactly(compressed);

byte[] decompressed = new byte[expectedSize];
int bytesWritten = LzfseDecoder.Decompress(
    decompressed,
    compressed,
    out DecompressStatus status);

if (status == DecompressStatus.Ok)
{
    outputStream.Write(decompressed, 0, bytesWritten);
}
```

## LZFSE Format Overview

LZFSE uses a block-based structure where each block has a magic number identifier:

| Magic | Description |
|-------|-------------|
| `bvx$` (0x24787662) | End of stream |
| `bvx-` (0x2d787662) | Uncompressed block |
| `bvx1` (0x31787662) | LZFSE compressed (uncompressed tables) |
| `bvx2` (0x32787662) | LZFSE compressed (compressed tables) |
| `bvxn` (0x6e787662) | LZVN compressed |

The decoder automatically detects and handles all block types.

## Limitations

- **Decode-only**: This library only supports decompression. For compression, use the original C library or platform-specific APIs on Apple platforms.
- **No streaming API**: Currently requires the entire compressed data in memory. Decompression cannot be resumed across multiple calls — if the destination buffer is too small the decoder returns `DecompressStatus.DestinationFull` and the caller must retry from the start with a larger buffer.
- **Buffer size**: The destination buffer must be large enough to hold the full uncompressed output. Over-allocating is safe; the decoder reports the actual byte count.

## License

This project is licensed under the BSD 3-Clause License - the same license as Apple's original LZFSE implementation. See the [LICENSE](LICENSE) file for details.

## References

- [Apple Compression Framework Documentation](https://developer.apple.com/documentation/compression)
- [LZFSE GitHub Repository](https://github.com/lzfse/lzfse)
