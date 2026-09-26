# Hinting benchmarks, and the baseline every hinting performance change is measured against

`docs/testing.md` described a `src/PeachPDF.Benchmarks` project that did not exist. The project is now `src/PeachDrawing.Text.Benchmarks` (the engine's benchmarks
belong beside the engine's tests, and like them read only the public API: `Typeface.TryGetOutline` with an `OutlineRequest`), and the page describes what it is.
Every later change to the hinting code (LRU cache, per-miss allocations, arithmetic) is shown against the numbers below, not against a reading of the code.

## What the benchmarks are

- **`HintingColdBenchmarks`**: a typeface is loaded afresh **outside the timing** (`[IterationSetup]`, its own `FontSet`, so the hinting engine and every cache in it
  start empty), then the iteration asks for every glyph of the font at 12, 16 and 40 ppem with `GridFitting.Standard`. `RunStrategy.Monitoring`, one invocation per
  iteration, 5 warm-up and 40 measured iterations. `[MemoryDiagnoser]`. The measuring thread has run the interpreter before, so what the engine keeps in
  thread-static state is warm: this is the steady state of a process that hints a lot, not its first glyph. Fonts: LiberationSans and LiberationSerif (real hinted
  TrueType, with composites), SourceSans3 (TrueType), HintingOpcodes (the opcode fixture), HintingCff and HintingCffCid (Adobe CFF).
- **`HintingHotBenchmarks`**: the working set (the first 300 glyphs with ink) is loaded once, then 1, 4 or 8 dedicated threads each make 500,000 lookups in an order
  of their own. `OperationsPerInvoke` is the lookups **per thread**, so the figure is time per lookup per thread: a cache that scaled perfectly would show the same
  number for 1 and for 8 threads, and what shows above it is the price of sharing.
- `dotnet run -c Release --project PeachDrawing.Text.Benchmarks -- --fonts` prints each font's glyph count and how many of them are grid-fitted at each size, so a
  benchmark that quietly measures the fallback to the scaled design (which is fast) cannot pass for one that measures hinting.

## Traps found by running it

- **BenchmarkDotNet's generated project inherits `src/Directory.Build.props`.** It sets `ImportDirectoryBuildProps=false` in the project body, which is read after
  the props were imported, so under `src/` the generated project got `net8.0;net10.0` and failed restore (`NU1201`) against a net10.0-only benchmark project. The
  benchmark project builds into the git-ignored `artifacts/` at the repository root instead. Setting `TargetFrameworks` empty in the benchmark's own csproj is
  not enough.
- The `[SimpleJob]` names the runtime (`RuntimeMoniker.Net10_0`): with the .NET 11 RC SDK on the machine BenchmarkDotNet otherwise generated a net8.0 project.
- `[ParamsSource]` needs a public property or method, not a field; and a `Font` parameter given as a file name is printed as `Liber(...).woff [27]`, so the
  parameters are short names mapped to files in `FontFiles`.
- No workflow builds the project (the CI jobs name their projects; none builds `PeachPDF.slnx`), it is `IsPackable=false`, net10.0-only, and outside the
  publish/AOT/pack paths. It is in `PeachPDF.slnx`, so it counts toward the zero-warning solution rebuild.

## The baseline (origin/main at the time; i9-14900K, .NET 10.0.12, Release)

**Read the timings as indicative:** other agents were building and testing on the same machine, which sat at 90-99% CPU while these ran. The medians below are
the ones to compare, and every later comparison is run against a baseline build of the same commit in the same session. **The allocation figures are exact.**

Cold, whole font loaded into an empty cache (median; the size makes no difference to allocation and none worth reading to time):

| Font (glyphs) | Time, 16 ppem | Allocated | Per glyph |
| --- | --- | --- | --- |
| LiberationSans (2620) | ~14.2 ms | 25.0 MB | ~9.8 KB |
| LiberationSerif (2602) | ~15.0 ms | 28.2 MB | ~11.1 KB |
| SourceSans3 (2478) | ~46.2 ms | 25.4 MB | ~10.5 KB |
| HintingOpcodes (391) | ~4.3 ms | 2.53 MB | ~6.6 KB |
| HintingCff (300) | ~5.7 ms | 6.36 MB | ~21.7 KB |
| HintingCffCid (150) | ~2.8 ms | 3.10 MB | ~21.2 KB |

Hot, cache hits (mean, ns per lookup per thread; nothing is allocated):

| Font | 1 thread | 4 threads | 8 threads |
| --- | --- | --- | --- |
| LiberationSans | 60 | 610 | 1,556 |
| HintingCff | 25 | 536 | 1,299 |

So a hit costs 25-60 ns alone, and the same hit costs 20-50 times that with eight threads on it: the cache's one lock (a hit takes it and reorders a linked list
under it) is the whole cost of a hit under contention, and is where the first change goes. A CFF glyph allocates about twice what a TrueType glyph does.

## Not done

Vectorization and `Math.BigMul`/`Int128` for the fixed-point arithmetic (two code reviews found nothing worthwhile: typical glyphs are 10-300 points, and the 26.6/16.16
multiply has no exact portable SIMD form). A rendering benchmark: `PeachPDF.TestHarness --benchmark` already times and measures whole-document renders.
