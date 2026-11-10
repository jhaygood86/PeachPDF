# Performance optimizations: what worked, and what measuring ruled out

A pass over the rasterizer, the text engine and the PDF writer, done as byte-identical changes: every optimization was pinned by a
golden hash (or a scalar-versus-vector test) recorded **from the code it replaced, before touching it**, and benchmarked against a
baseline taken before the edit. The tooling that made that possible is in the tree: `TestHarness --benchmark` (with
`--benchmark-filter`, `--benchmark-json`, GC pause and Save time), `--raster-benchmark` (every showcase painted onto the CPU
`RasterCanvas`), `harness/PeachDrawing.Benchmarks`, `PeachDrawing.Text.Benchmarks` (`--cold-start` too), and the golden tests
(`KernelGoldenTests`, `ScanlineGoldenTests`, `GradientGoldenTests`, `ImageGoldenTests`, `FilterOpsGoldenTests`,
`UnicodeTableGoldenTests`, `CmapLookupGoldenTests`).

## What landed

- Scanline rasterizer: per-thread edge scratch, run-writing in the row resolve, `PolygonSet` on arrays (page-sized fill 1.6 -> 0.7 ms).
- Opaque-run `BlendSolid`, `DropShadow` through the Porter-Duff kernel, no coverage copy in `CompositeRow`.
- cmap lookups from a lazily filled 256-codepoint page table (2-10x on a corpus pass).
- Color matrix, `feTurbulence`, `feComposite` arithmetic on vectors (44%, 3.4x, 33%); gradient segment constants hoisted; image
  sampling (whole-pixel draws as row copies, vector block averaging); morphology scratch from the pool.
- Direct tables below U+2100 in front of the script, bidi class and segmentation binary searches (about 10x per lookup, 32-50% on
  paragraph analysis).
- Win2D blurs and shadows each direction by its own deviation (a horizontal then a vertical directional blur) instead of by the mean.

## Traps found by running it

- **A binary search can be slower than the scan it replaces.** The cmap segment scan exits within a few segments for ASCII, so a binary
  search cost 664 -> 1177 ns per corpus pass there. The fix that won was a page cache, not a better search.
- **Unsigned vector conversions are emulated on x64.** The first color matrix kernel used `ConvertToSingle`/`ConvertToUInt32` on `uint`
  lanes and ran *slower* than scalar (9.1 vs 6.9 ms); the signed conversions are one instruction each (3.9 ms).
- **Integer divide is cheap on a modern x64 core.** Replacing the integer division in `ConvertColorSpace` with a single-precision divide
  (exact, but with conversions) went 5.1 -> 6.6 ms and was dropped. Likewise hoisting tap bounds in the exact-kernel blur was slower.
- **A sampling profile of the render thread lied about GC and hashing.** It put 35% in GC suspension and 20% in
  `RuntimeHelpers.GetHashCode`. Measured directly GC pause is 2-13% of a render, and switching the style transition cache off made
  rendering 17% *slower* on `svg`. Trust `GC.GetTotalPauseDuration` and an A/B over a profile's self time.
- **The PDF writer is not where the time goes.** Save is 5-17% of a render and GC 2-13%; the rest is parse, cascade, layout and paint.
  Removing per-operator `AppendFormat` and xref `string.Format` changed allocation by 0.1-0.5% and was reverted.
- **BenchmarkDotNet rebuilds from source for each job.** Editing or building the project under test while a benchmark runs can change
  the code being measured; record the baseline, let it finish, then edit.
- Float arithmetic can be vectorized bit-exactly if every operation keeps its order and nothing is fused (separate multiply and add,
  no `FusedMultiplyAdd`); keep a scalar reference reachable (`internal`) and test the two against each other on random data.

## Not done

- CoreGraphics still blurs by the mean of its two sigmas (Core Image takes one radius); the code only builds and runs on macOS.
- The GPU effect seam (`LayerEffect`) is already closed over three effects that all three screen backends apply natively; a capability
  query for individual effects has nothing to answer until a fourth effect exists, so none was added. SVG filter primitives beyond
  blur/shadow/color matrix, `preserve-3d` warps and flattening still run on the CPU `RasterCanvas`; Skia's retained layers rasterize
  on the CPU on purpose (a GPU render target hung an Android emulator frame, see `SkiaRetainedLayer`).
