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

The decoder is fully stream based: it reads compressed input from a source
`Stream` and writes decompressed output to a destination `Stream`.

```csharp
using LzfseSharp;

using FileStream source = File.OpenRead("data.lzfse");
using FileStream destination = File.Create("data.bin");
LzfseDecoder.Decompress(source, destination);
```

An asynchronous overload is also available:

```csharp
await LzfseDecoder.DecompressAsync(source, destination, cancellationToken);
```



## Limitations

- **Decode-only**: This library only supports decompression. For compression, use the original C library or platform-specific APIs on Apple platforms.

## License

This project is licensed under the BSD 3-Clause License - the same license as Apple's original LZFSE implementation. See the [LICENSE](LICENSE) file for details.

## References

- [Apple Compression Framework Documentation](https://developer.apple.com/documentation/compression)
- [LZFSE GitHub Repository](https://github.com/lzfse/lzfse)
