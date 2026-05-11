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

```csharp
using LzfseSharp;

byte[] compressed = File.ReadAllBytes("data.lzfse");
byte[] decompressed = LzfseDecoder.Decompress(compressed);
```

The decoder pre-scans the block headers to determine the exact decompressed
size, then performs a single decode pass into an exact-fit array. On truncated
or malformed input it throws `System.IO.InvalidDataException`.

### Decoding into a caller-owned buffer

If you already own a destination buffer, or want to distinguish truncated input
from malformed input without catching an exception, use the `Span<byte>`
overload and inspect `DecompressStatus`:

```csharp
byte[] dst = new byte[expectedSize];
int written = LzfseDecoder.Decompress(dst, compressed, out DecompressStatus status);

string message = status switch
{
    DecompressStatus.Ok              => $"Decompressed {written} bytes",
    DecompressStatus.SourceTruncated => $"Input truncated; {written} bytes recovered",
    DecompressStatus.DestinationFull => "Destination buffer was too small",
    DecompressStatus.Malformed       => "Input is not valid LZFSE",
    _                                => "Unknown status"
};
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
- **No streaming API**: Requires the entire compressed data in memory. Decompression into a caller-supplied buffer cannot be resumed across multiple calls — if the buffer is too small the `Span<byte>` overload returns `DecompressStatus.DestinationFull` and the caller must retry from the start with a larger buffer.

## License

This project is licensed under the BSD 3-Clause License - the same license as Apple's original LZFSE implementation. See the [LICENSE](LICENSE) file for details.

## References

- [Apple Compression Framework Documentation](https://developer.apple.com/documentation/compression)
- [LZFSE GitHub Repository](https://github.com/lzfse/lzfse)
