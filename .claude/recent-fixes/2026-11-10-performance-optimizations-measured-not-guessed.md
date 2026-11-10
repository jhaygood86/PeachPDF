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

## Later findings (second pass)

Landed: vector image masks (`DrawImageMasked`/`DrawImageAlphaMasked` 13-16 -> 2.5-2.9 ms), radial gradient positions four at a time (about 20%),
font table tags compared as integers (8% less allocation in the scan).

Measured and left alone:
- **Parallel scan of installed fonts.** 2.4x faster warm (12.5 -> 5.2 ms for 342 files) and identical results, but a process scans once and in a
  fresh process JIT and discovery dominate: cold-start did not move (44-55 ms). The Native AOT CLI, with no JIT, is where it could pay; an AOT publish
  could not be linked from this shell (`vswhere` not on the path), so it was not measured.
- **Glyph path memoization.** Does not appear in a profile of text-heavy raster renders, and caching flattened paths could not be byte-identical
  (flattening happens after the translation, so the last bits differ).
- **CMYK output conversion** (2.3 ms for 1024x768): only the PDF/X-1a path.
- **Parallel stream compression and PDF writer allocations:** Save is 5-17% of a render.
- **CSS lexer, hyphenation, `TextTransformer`, paragraph analysis fast paths:** none shows in a profile of stylesheet-heavy showcases; the cascade (about
  19%) and paint are where the time is. Paragraph analysis over a whole page is about 2% of a render.
- **Embedding a large RGBA PNG:** about 51% of the render is `PeachImage`'s `PngAlphaSplit`, in a separate package; the BMP round trip in `ReadBgraBitmap`
  is not on that path at all.

## Raster-fallback images without a PNG round trip (taken)

In `box_shadow` (and any document with raster fallbacks: blurred shadows, filters, backdrop filters, flattened transparency) `PdfCanvas.DrawRaster` was
about 41% of the render: `RasterEmbedding.ToXImage` PNG-encoded the surface (24%), and `PdfRasterImage.FromStream` read the PNG back (13%) to pass its IDAT
through and to split, inflate and re-deflate the alpha plane. `RasterPlaneCodec` now builds the streams of that pass-through embed straight from the pixels
(unpremultiply, RGB and alpha planes, Sub-or-Up row filter, one Flate stream each) behind `RasterPassthroughImageSource`. Interleaved A/B on the same
machine: `box_shadow` -17%, `css_filter_raster` -13%, `shadows_raster` -23%, `svg_filters_raster` -21%, allocation down 19-43%.

It cannot be byte-identical (the compressed streams differ), so it was verified by rasterizing every page of the 27 PDFs that changed with PDFium and
MuPDF before and after: 0 differing pages, PDFs 13% smaller in total. Traps the pixel check found, each of which a byte check could not have:
- **Hard-edged transparency** (every pixel opaque or clear) is a *color key* in the PNG route (a palette image with `tRNS`), and viewers composite a color
  key differently from an RGB image with a soft mask (MuPDF smooths the mask's edge; `raster_antialiasing_off` is meant to be crisp). It keeps the PNG route.
- **An opaque surface of 256 colors or fewer** is a palette image, embedded `/Indexed`; MuPDF resamples that differently from `/DeviceRGB` when scaling
  (9417 differing bytes with identical decoded pixels). It keeps the PNG route.
- A first version was 28% *slower* on `box_shadow` and allocated 48% more: the few-colors check did a hash insert per pixel of flat surfaces, and the
  filtered planes were fresh arrays with a half-size compression buffer. Skipping runs of equal pixels, renting the planes, and starting the output at an
  eighth of the input fixed both. Always A/B interleaved, new and old alternating on the same build state: absolute times on this machine drifted by 2x.

## GPU effect API (third pass)

Landed: `RetainedLayer.Draw(target, opacity, effects)`: layer effects (blur, shadow, color matrix) applied to a kept recording each time it is drawn, so an
animated blur or shadow costs a composite per frame and not a repaint; the default is built on `BeginLayer` and runs on every canvas (Win2D and Skia apply the
effect natively on the kept texture). Not landed, deliberately, with the reasons so they are not relitigated without new information:
- **New `LayerEffect` types and a multi-input effect graph** (morphology, convolution, lighting, displacement, feComposite/feBlend between two sources): nothing in
  the repo would call them yet (the SVG filter evaluator works on CPU pixels and would need a rewrite to a graph), and there is no GPU here to show a benefit. The
  effect set is closed and every screen backend applies all three existing effects natively, so a per-effect capability query has nothing to answer.
- **A public `Canvas.TryApplyEffects(RasterSurface, ...)`:** its only caller would be code that already holds CPU pixels, which would pay a GPU upload and readback to
  run an effect the CPU filters already run.
- **`DrawImageProjected` for `preserve-3d`:** needs perspective in each backend's image drawing; not verifiable without the GPUs.
- **Van Herk morphology, a downsampled-image cache, conic gradient vectorization:** feMorphology at large radii is rare, a repeated strongly shrunk image saves
  about a millisecond per repeat, and conic needs `Atan2`, which has no bit-exact vector form.

## 256-bit kernels and the ARM64 macOS coverage gate

`Vector256.IsHardwareAccelerated` is false on ARM64, so a kernel written only with `Vector256<double>` runs the scalar code on Apple Silicon (and on any Arm
machine), and the macOS CI leg, which is ARM64, never executes it: the diff-coverage gate measured `Turbulence.Vector.cs` at 0% there and failed the PR at 78%. The
rule now: every double-precision kernel has a 128-bit form (SSE2 on x64, NEON on ARM64; `*.Vector.cs` or inline) and, where it pays, a 256-bit form in its own
`*.Vector256.cs` file; production dispatches 256 -> 128 -> scalar by `IsHardwareAccelerated`; tests call each form directly against the scalar reference
(`DoubleKernelVariantTests`) and the 256-bit tests skip where 256-bit vectors are not accelerated; and the macOS leg of the diff-coverage gate passes
`--exclude '*.Vector256.cs'` (`.github/workflows/test.yml`), so Windows and Linux gate the 256-bit files and macOS gates the 128-bit ones. On x64 the 256-bit form is
worth keeping: turbulence 27.5 ms against 34.1 ms for the 128-bit form, arithmetic 6.6 against 7.8 ms; the radial gradient is the same either way (the stop lookup,
not the arithmetic, dominates).
