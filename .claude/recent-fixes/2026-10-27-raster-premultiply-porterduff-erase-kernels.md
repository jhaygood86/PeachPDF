# Raster: premultiply, Porter-Duff, opacity and erase moved onto exact integer vector kernels

Found by reading, not profiling: every image decode and every PNG/JPEG save ran `PixelMath.Premultiply`/`Unpremultiply`
one byte at a time (Unpremultiply with three integer divisions per pixel), `FilterOps.Arithmetic` allocated a
`double[4]` per pixel, `FilterOps.PorterDuff` switched on a string inside its per-channel loop, and
`RasterCanvas`'s opacity scale and `Erase` were scalar per byte.

## What changed
- `PixelMath.Premultiply`/`Unpremultiply` (`PeachDrawing.Core`) have `Vector128` paths. Premultiply is the same
  `Div255` integer math as `PixelKernels`. Unpremultiply divides in float32: the numerator is at most 65152 (exact in a
  float) and a non-integer quotient is at least 1/255 from the next integer, far more than IEEE division's 2^-24
  relative error, so truncating the float quotient equals the integer division on every CPU. Pinned by an exhaustive
  test over every colour/alpha pair (`PixelMathTests`), not an argument alone.
- `PixelKernels.PorterDuff` (new `PixelKernels.Compositing.cs`) evaluates all five operators as
  `Div255(a * fa) + Div255(b * fb)`: over uses a factor of 255 (exact), in/atop use alpha(b), out/xor its complement. It also
  serves `FilterOps.OverInPlace` and the Normal branch of `FilterOps.Blend`, with `skipTransparentSource` keeping the old
  "alpha 0 leaves the destination alone, whatever the colour bytes hold" behaviour.
- `ScaleCoverage` (opacity) and `EraseSpan` kernels replace the scalar loops in `CompositeRow` and `EraseSink`.
- `FilterOps.Arithmetic` reuses one stack span (same double expression order); `Tile`/`MirrorPad` copy whole row runs;
  `SolidPaint.FillSpan` and `RasterSurfaceEncoding.IsOpaque` use packed 32-bit operations; the multi-matrix
  `ColorMatrixFilter.ApplyInPlace` no longer allocates an interface enumerator per pixel.

## Deliberately not done
- `ColorMatrixFilter` is still scalar float: a Vector128 form must keep separate multiply-then-add in the same order with
  no FMA, and its early-outs are not free (the offset applies to transparent pixels too). Needs a rasterized golden
  comparison first.
- `BitmapPaint`/`TilePaint` sharing `PixelKernels.BilinearTexels`, and incremental u/v stepping: stepping changes double
  rounding versus `Affine.Apply(x)`, so it needs a golden corpus.

## Trap
A `PixelKernels` enum used in a public test signature fails with CS0050/CS0051 (the kernels are internal); the theories
take the enum as an `int` and cast.

Evidence: PeachDrawing.Core.Tests 165, PeachDrawing.Tests 286, PeachBrackets.Engine.Tests 16389, PeachPDF.Tests 1490 (+9
skipped) pass in Release on net8.0; zero-warning Rebuild of the four touched projects. No benchmark was run.
