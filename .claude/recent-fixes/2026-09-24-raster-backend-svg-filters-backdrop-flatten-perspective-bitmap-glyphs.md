# The raster backend's second wave: SVG filters, backdrop-filter, flattening, perspective, bitmap glyphs

Builds on [the foundation](2026-09-23-raster-backend-foundation.md). What was load-bearing, and what was only found by running it.

**One machinery, five consumers.** Every feature here is the same three moves: ask the current graphics for a surface
(`BeginRasterSurface`), paint into it with the ordinary painter, hand the bitmap back (`DrawRaster`). What differs is what gets painted
into it: the element's own content (SVG filters), an earlier repaint of the page (backdrop-filter, flattening), or an untransformed copy that
is then warped (perspective). Anything that needs "what was painted before X" reuses one repaint: a second `FragmentPainter` with a stop
point. That replaced the plan's `TeeGraphics` mirror (every RGraphics call forwarded to a PDF and a raster twin, dirty-rect bookkeeping,
twin images): no forwarding class, nothing to keep in lock-step with the RGraphics surface, and it works on any page with no pre-scan.

**Measured, not assumed.**
- CTM scale matters for resolution. `GraphicsAdapter` now tracks the accumulated linear part of pushed transforms so a raster region under
  `transform: scale(3)` or an SVG `viewBox` magnification gets the pixels it needs (`RGraphics.TransformScale`). The plan had recorded this
  as an accepted gap; it turned out to be a dozen lines.
- SVG `<text>` under a filter was blank until the unmeasurable-bounds fallback (see the invariant of that name): the region had been 1.2 x 1.2
  units. It looked like a raster text bug for half an hour.
- Two separately snapped, adjacent flattened bitmaps can show a one-device-pixel seam in PDFium: the second image's edge pixel is a
  downsample of only *its* columns, so it lightens the neighbour's edge. Pixel data in the overlap is identical (verified by extracting both
  images); it is the viewer's image resampling. Accepted, and documented for users.
- At `PixelsPerInch != 72` the SVG filter *region* is scaled by `PixelsPerPoint` in both the vector and the raster evaluation (a
  pre-existing convention mismatch in how the SVG renderer converts user units); the raster path reproduces the vector one exactly rather
  than fixing it, so the two agree. Not investigated further.
- An `<hr>`-like zero-area rectangle and an anonymous text box's `WholeBoxRect` both dragged bitmap extents to the origin: see the invariant.

**Deliberately not done.** `transform-style: preserve-3d` (done afterwards: [3D rendering contexts](2026-09-24-preserve-3d-rendering-context.md)),
flattening under a transformed ancestor, `feImage`, SIMD paths wider than `Vector128` (see the benchmark note below), and per-pixel
`kernelUnitLength` for `feConvolveMatrix`.

**Evidence.** Full net8.0 suite green (14,000+ tests); each renderer-visible feature was rasterized with PDFium and MuPDF and compared
(SVG filter gallery, backdrop panels, a PDF/A-1b flattened page against the same page as vector PDF/A-2, perspective cards); the PDF/A tests
assert no `/SMask`, `/ca`, `/BM` or transparency group anywhere in the file.

## The vectorisation benchmarks (recorded because they overturned two assumptions)

Measured in Release with tiered compilation off, on an AVX2 machine, 1 M pixels (kernels) and a 2000 x 2000 RGBA surface (filters):

| kernel | scalar | Vector128 | Avx2 |
|---|---|---|---|
| `BlendSolid` | 6.1 ms | 0.82 ms (7.5x) | 0.54 ms (1.65x over Vector128) |
| `BlendSpan` | 7.4 ms | 0.84 ms (8.9x) | 0.73 ms (1.6x over Vector128) |
| `MultiplyCoverage` | 0.49 ms | 0.07 ms (6.5x) | not needed |
| box blur, 4 Mpx | 228 ms | 36 ms (6.3x) | not needed |
| exact-kernel blur (sigma < 2), 4 Mpx | 458 ms | 75 ms (6.1x) | not needed |
| morphology radius 4, 4 Mpx | 682 ms | 12.7 ms (54x) | not needed |

- **The first `Vector128` blend kernels ran at scalar speed.** An index vector passed to `Vector128.Shuffle` in a *local* was not seen as a
  constant by the JIT, so it emitted a per-element software shuffle (5.7 ms, 50x a memcpy). Written as the literal `Vector128.Create(...)` at the
  call site it lowers to one byte shuffle. The equality tests could not catch this (the bytes were right); only the stopwatch did. It is
  written into the `PixelKernels` header comment.
- **`Vector256.Shuffle` is not the answer either**: the portable 256-bit shuffle measured 0.19x of `Vector128` (a software path again); the
  `Avx2.Shuffle` (`vpshufb`) form is 1.6x. So the wide path is x86-only, behind `Avx2.IsSupported`, and clears the 1.3x bar the plan set.
- The box-blur division is a correctly rounded IEEE single-precision divide followed by truncation, argued exact (sums < 2^20, quotients at
  least 1/4095 from an integer, floats space 2^-16 there) and pinned by scalar-vs-vector equality tests over sizes, offsets and shapes.
- Not shipped: `Vector512` (no hardware to measure on), Arm/WASM-specific paths (`Vector128` already lowers to NEON / SIMD128), and
  `System.Numerics.Tensors` (every kernel that mattered is byte-oriented and integer-exact, where `TensorPrimitives` has nothing to offer).


## What the post-change review found (all fixed, each with a regression test)

- **A projective plane wholly behind the eye painted as a page-covering smear.** `ResolveProjective` negated any map whose centre had
  `w < 0`, which reflects the behind-the-eye region through the origin. The sign of `w` is meaningful and `ProjectRectangle` already clips
  at `w > 0`; the negation is gone.
- **`feDropShadow` handed `DropShadow.Apply` a straight colour where it wants a premultiplied one**, so a grey flood at partial opacity
  rendered too light (or vanished). It now premultiplies, as the CSS `drop-shadow()` path does.
- **`backdrop-filter: blur(<huge>)` allocated a mirror-padded surface with no bound.** The pad is now capped at the surface size and
  halved until the padded surface fits `MaxRasterPixels`; the box blur's window is capped at the surface extent so a huge sigma can neither
  overflow a cast nor dilute the result to nothing.
- **Flattened PDF/X-1a bitmaps ignored `BlackGeneration`**: neutral pixels are now generated the way `PdfXColorSpaceGuard` generates neutral
  vector colours, so a flattened region's black matches the black around it.
- **Default primitive subregion** is now the union of the subregions of the results it reads (Filter Effects 1), not always the whole filter
  region; a missing `x`/`y`/`width`/`height` takes that default's edge. `feTurbulence` stitches to its own subregion and truncates its seed.
- **Out-of-range `double` to `int` casts** in the transfer-function LUT and the blur window are clamped first: an unbounded cast is
  runtime- and CPU-dependent, which the bit-identical rule forbids.
- **`BitmapGlyphSource.HasGlyph`** now treats a malformed CBLC/sbix table like `TryGet` does (no picture) instead of throwing out of font
  embedding.
