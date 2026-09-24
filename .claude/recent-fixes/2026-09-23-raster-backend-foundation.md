# A raster `RGraphics` backend, and the first thing it draws: `filter: blur()` and the colour functions

**The load-bearing idea.** The raster backend is a *subordinate* of a PDF render, not a second pipeline. Layout and text
measurement stay on the PDF adapter (so metrics cannot drift); paint asks the current `RGraphics` for a pixel surface only
where a subtree needs one, paints the *same* fragment subtree into it, post-processes pixels, and hands the bitmap back to be
embedded. `RasterGraphics` reads the PDF adapter's pens/brushes/fonts/paths directly (same assembly, plain data) instead of
duplicating that layer. The new `RGraphics` members are `virtual` with inert defaults, because ~11 test doubles subclass it.

**What running it showed (not visible from reading):**

- `DownscaleImages` (on by default) would have resampled every bitmap back to on-page size, silently undoing the DPI.
  `ImageSelector`/`ComputeTargetPixelSize` needed an explicit `IsRasterOutput` exemption; under `ImageCompression.Lossless`
  the existing PNG pass-through pinning does *not* protect it. See
  [../invariants/raster-a-bitmap-is-placed-at-its-own-snapped-rectangle.md](../invariants/raster-a-bitmap-is-placed-at-its-own-snapped-rectangle.md).
- `PdfAUnimplementedTransparencyFeatureTests` pinned `filter: blur()` as a no-op under PDF/A-1 and *failed on purpose* the
  moment it rendered — its remarks say the fix is to guard the new paint path and move the case to the "throws" theory, not
  loosen the pin. `GraphicsAdapter.DrawRaster` now calls `PdfATransparencyGuard` with a message naming the CSS feature.
- Building the whole solution needs a `cref` to be fully qualified (`System.ArgumentOutOfRangeException`) in
  `PdfGenerateConfig`, which has no `using System` — a zero-warning rebuild caught it.
- NativeAOT: `dotnet publish -p:PublishAot=true` compiles the raster code with no trim/AOT warnings; on this machine the link
  step needs `vswhere.exe` on `PATH` (Visual Studio's `Installer` directory) — an environment issue, not a code one.

**Deliberately not done here.** Selectable text inside a rasterized element (an invisible-text overlay), `text-shadow`, the
Gaussian `box-shadow`/`drop-shadow` silhouette, and the SVG `<filter>` primitives are separate milestones. Wider-than-128-bit
kernel paths and platform intrinsics are benchmark-driven and not added yet.

**Evidence.** 120 raster unit/integration tests (rasterizer coverage to the pixel, stroker, scalar-vs-`Vector128` equality over
random inputs and every tail length, blend modes, gradients, tiles, masks, blur conservation/symmetry, every filter matrix,
and end-to-end DPI/placement across four PPI/DPI combinations); full net8.0 suite 13,719 passed; 176 CLI tests; solution
rebuild with 0 warnings; the AOT-published CLI rendered a blurred/greyscale document; both PDFium and MuPDF rasterize the
result identically to the eye, sharp at 600% zoom.
