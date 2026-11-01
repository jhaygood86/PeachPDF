# PeachDrawing.Skia, and the switchable image codec on both native backends

**What landed.** A new `PeachDrawing.Skia` package (SkiaSharp `Canvas`), and `Win2dImageCodec` on `PeachDrawing.Win2D`. Both backends now
decode images with PeachImage by default and can be switched to the platform's own codec (Skia's `SKCodec`, Windows' WIC), falling back to
PeachImage for what the platform cannot read.

**The load-bearing idea: the consumer supplies the surface.** The motivation is GPU rendering on .NET Android, but nothing in the package is
Android-specific, and nothing creates a surface or GPU context. `SkiaCanvas.FromCanvas` borrows an `SKCanvas`; acceleration is whatever
backs it. That is why layers are `SaveLayer`, tiles are `SKPictureRecorder` pictures and a pushed clip/transform is a `Save` level: all of it
stays in the caller's canvas and unwinds with `RestoreToCount`, so disposing the `SkiaCanvas` leaves the host's save level, matrix and clip
exactly as found. A host matrix (DPI scale, zoom) is kept, and `SkiaLinkMap.HitTestDevice` exists because a pointer on such a host arrives in
the SKCanvas's own pixels, not in layout units.

**Found by running it, not by reading it.**

- `SKColors.Transparent` is `#00FFFFFF`, not `#00000000`: a transparent surface pixel read back never equals it. Tests compare alpha.
- Reading pixels from a `SkiaComposer` finishes it (the canvas is disposed), so a later call on the canvas throws; order the assertions.
- The colour-matrix translation column is normalised (0..1) in the SkiaSharp 3 / current Skia, not 0..255 - pinned by
  `A_colour_matrix_offset_is_added_to_the_colour`.
- A layer's content canvas must be a child over the *same* `SKCanvas`, with its own base save level above the layer's: handing the caller the
  parent canvas itself would let a `using` on `CanvasLayer.Canvas` dispose it.
- The Win2D native decode must not block a UI thread on the WinRT operation directly; it runs on the thread pool and is waited for
  (`Native_decoding_does_not_deadlock_a_single_threaded_caller`).

**Deliberately not done.**

- No per-glyph path cache for the PeachDrawing text engine: `SKPath` is not safe to read from two threads while it computes bounds lazily,
  and a context is shared across threads. Glyph outlines are rebuilt per run (as on Win2D and SVG). Measure before adding one.
- No default link launcher: a platform-neutral package cannot open a URI. The host supplies an `ISkiaLinkLauncher` or handles
  `LinkActivating`.
- No dependency on `SkiaSharp.HarfBuzz`: native-font shaping is the `ISkiaTextShaper` seam (a basic built-in shaper by default; a test supplies a real HarfBuzz one to prove the seam). Do not add the dependency back - a host that wants it references it itself.
- Native-font mode does not pass `font-feature-settings` to HarfBuzz and returns no outlines (`GetTextOutline` is `null`); documented in the
  guide's table, not a defect.
- The GPU path is not exercised in CI (no GPU context there); every test draws on a CPU surface the test supplies. A `GRContext`-backed
  surface should be checked by hand on a device.

**Evidence.** `PeachDrawing.Skia.Tests` (86 tests, net8.0) and `PeachDrawing.Win2D.Tests` (156 tests, with the new codec tests) pass; the
example's PNGs were looked at (gradients, conic, dashes, a faded group with a drop shadow, text).
