# LzfseSharp.Fuzz

Fuzz harness for `LzfseDecoder.Decompress`. The harness asserts a single invariant:
for **any** input bytes, the decoder either returns normally with a valid
`DecompressStatus` and a byte count in `[0, dstBuffer.Length]`, or throws nothing
at all. Unhandled exceptions, out-of-range return values, and non-deterministic
`Ok` decodes are all treated as crashes.

## Modes

### `--smoke` (default)

Runs 10,000 iterations in-process with a freshly-chosen random seed. The seed
is printed on start-up so any failure can be reproduced via `--seed <N>`.

```
dotnet run -c Release --framework net10.0 --project LzfseSharp.Fuzz -- --smoke
```

Output on success:
```
Smoke run: seed=994176900 iterations=10000
OK: 10000 iterations, no invariant violations.
```

To replay a specific seed:
```
dotnet run -c Release --framework net10.0 --project LzfseSharp.Fuzz -- --smoke --seed 994176900
```

Good for CI and local verification; won't catch deep bugs that require millions
of executions per second, but catches the class of crash-on-malformed-input
bugs the decoder is most exposed to.

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

- **bvx2 with 28–31 bytes of fixed header** threw `ArgumentOutOfRangeException`
  from a span slice — the outer guard checked for 28 bytes but `DecodeV2ToV1`
  needs 32. Regression test: `LzfseDecoderTests.Decompress_V2BlockWithIncompleteFixedHeader_ReportsTruncated`.
- **LZVN block with under-sized `PayloadByteCount`** (large-literal opcode
  claiming more bytes than the declared payload) threw `IndexOutOfRangeException`
  from `CopyLiteralBytes`' partial-copy branch. Regression test:
  `LzvnDecoderTests.Decompress_LzvnBlockWithUnderSizedPayload_DoesNotCrash`.
