# LzfseSharp

A C# port of Apple's [LZFSE](https://github.com/lzfse/lzfse) (Lempel-Ziv Finite State Entropy) compression algorithm. This library provides **decoding-only** functionality for decompressing LZFSE-compressed data.

## What is LZFSE?

LZFSE is a Lempel-Ziv style data compression algorithm using Finite State Entropy coding, introduced by Apple with OS X 10.11 and iOS 9. It targets similar compression ratios to DEFLATE but with significantly higher compression and decompression speeds.

## Features

- ✅ Pure C# implementation - no native dependencies
- ✅ Supports all LZFSE block types:
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

// Read compressed data
byte[] compressedData = File.ReadAllBytes("data.lzfse");

// Allocate buffer for decompressed output
// You need to know the uncompressed size beforehand
byte[] decompressedData = new byte[uncompressedSize];

// Decompress
int bytesWritten = LzfseDecoder.Decompress(
    decompressedData,
    compressedData
);

if (bytesWritten == 0)
{
    Console.WriteLine("Decompression failed!");
}
else
{
    Console.WriteLine($"Decompressed {bytesWritten} bytes");
}
```

### Working with Streams

```csharp
using LzfseSharp;

using var inputStream = File.OpenRead("data.lzfse");
using var outputStream = File.OpenWrite("data.bin");

// Read compressed data
byte[] compressed = new byte[inputStream.Length];
inputStream.Read(compressed, 0, compressed.Length);

// Decompress
byte[] decompressed = new byte[uncompressedSize];
int bytesWritten = LzfseDecoder.Decompress(decompressed, compressed);

// Write decompressed data
if (bytesWritten > 0)
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
- **No streaming API**: Currently requires the entire compressed data in memory. Streaming support may be added in future versions.
- **Buffer size**: You must allocate the output buffer with the correct uncompressed size beforehand.

## License

This project is licensed under the BSD 3-Clause License - the same license as Apple's original LZFSE implementation. See the [LICENSE](LICENSE) file for details.

## References

- [Apple Compression Framework Documentation](https://developer.apple.com/documentation/compression)
- [LZFSE GitHub Repository](https://github.com/lzfse/lzfse)
