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

### `--extended --seeds a,b,c,... [--iterations N]`

Long-running in-process fuzz. Runs `N` iterations (default 5,000,000) per seed
over the supplied list. Uses the same input generator and invariants as
`--smoke`, just a lot more of them. Intended for manual / scheduled runs —
there is a `Fuzz (extended)` GitHub Actions workflow with `workflow_dispatch`
that invokes this.

```
dotnet run -c Release --framework net10.0 --project LzfseSharp.Fuzz -- \
    --extended --seeds 1,2,3,4,5,6,7,8,9,10 --iterations 30000000
```

Throughput in Release is ~2.4M iter/sec steady-state on a modern x64 CPU.
The workflow default (10 seeds × 30M iterations = 300M total) takes about 2
minutes of fuzzing wall-clock time.

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
