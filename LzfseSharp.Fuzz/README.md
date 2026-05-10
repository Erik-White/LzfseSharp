# LzfseSharp.Fuzz

Fuzz harness for `LzfseDecoder.Decompress`. The harness asserts a single invariant:
for **any** input bytes, the decoder either returns normally with a valid
`DecompressStatus` and a byte count in `[0, dstBuffer.Length]`, or throws nothing
at all. Unhandled exceptions, out-of-range return values, and non-deterministic
`Ok` decodes are all treated as crashes.

## Modes

### `--smoke` (default)

Runs 10,000 fixed-seed iterations in-process. No external tooling required.

```
dotnet run -c Release --framework net10.0 --project LzfseSharp.Fuzz -- --smoke
```

Exits 0 with `OK: 10000 iterations, no invariant violations.` on success, or 1
with the offending inputs printed to stderr on failure. Good for CI and local
verification; won't catch deep bugs that require thousands of executions per
second, but catches the class of crash-on-malformed-input bugs the decoder is
most exposed to.

### `--libfuzzer`

SharpFuzz libFuzzer entry point. Requires instrumentation of the compiled
assemblies via the [sharpfuzz CLI tool](https://github.com/Metalnem/sharpfuzz)
and a libFuzzer runtime (typically from a recent Clang toolchain).

1. Install the CLI:

   ```
   dotnet tool install --global SharpFuzz.CommandLine
   ```

2. Build the fuzz project in Release:

   ```
   dotnet publish -c Release --framework net10.0 LzfseSharp.Fuzz/LzfseSharp.Fuzz.csproj
   ```

3. Instrument the target assembly:

   ```
   sharpfuzz LzfseSharp.Fuzz/bin/Release/net10.0/publish/LzfseSharp.dll
   ```

4. Run libFuzzer against the published harness directory, with an initial
   corpus seeded from `LzfseSharp.Tests/` fixtures or reference-encoded outputs.

### `--afl`

SharpFuzz out-of-process AFL++ entry point. Same instrumentation workflow as
`--libfuzzer`; drive it with `afl-fuzz` against the published harness.

## Why a separate project

- SharpFuzz instrumentation rewrites the target assembly in place, which
  invalidates strong-name signatures. The main `LzfseSharp` project is signed;
  this one is not.
- Continuous fuzzing is an opt-in activity. Keeping the harness out of the test
  project keeps the standard `dotnet test` flow fast.
- The smoke mode still runs in-process and exercises the harness invariants,
  so local-dev workflow stays simple.

## Historical finds

The `--smoke` mode found its first bug on its first run: a bvx2 block with
28–31 bytes of fixed header data threw `ArgumentOutOfRangeException` from a
span slice (the outer guard checked for 28 bytes; `DecodeV2ToV1` needs 32).
Regression test: `LzfseDecoderTests.Decompress_V2BlockWithIncompleteFixedHeader_ReportsTruncated`.
