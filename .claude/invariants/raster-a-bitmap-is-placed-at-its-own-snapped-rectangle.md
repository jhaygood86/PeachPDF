# A raster surface is placed at the rectangle it records, never at the one that was asked for

`RasterSurfaceFactory.Create` snaps the requested layout bounds **outwards** to whole pixels on one grid (pixel pitch
`dpi / 72 / PixelsPerPoint` per layout unit, anchored at the local origin), and the surface stores the snapped rectangle
as `RasterSurface.LayoutRect`. `DrawRaster` must place the bitmap at *that* rectangle.

Placing it at the originally requested bounds looks harmless — it differs by less than a pixel — but it stretches the
bitmap by that sub-pixel amount, so every pixel stops being exactly `1/dpi` inch, and the stretch is a resample: a blurred
edge gets a second, uneven softening. Two neighbouring surfaces would also stop abutting, leaving a seam. The measured
symptom of getting it right is `imagePixelWidth * 72 / dpi == placed width in points` to two decimals
(`RasterFilterIntegrationTests.BlurredBox_IsAnExactPhysicalSize_AtTheConfiguredDpi`); a 501 px bitmap at 300 dpi is placed
at exactly `120.24 pt`.

Related traps in the same area:

- The DPI is a **physical** resolution, so the pixels-per-layout-unit factor is read from `PixelsPerPoint` **at the moment
  the surface is created**. `ShrinkToFit`/`ScaleToPageSize` change `PixelsPerPoint` after layout; a factor computed from
  `PixelsPerInch` would give the wrong physical DPI on a scaled document.
- A **tile** (`RasterGraphics.CreateTile`) is different on purpose: its pitch is stretched by less than one pixel so it covers
  its requested size exactly, and it is drawn back at that size. That is why `RasterSurface` has separate X and Y pitches.
- The image downscaler (`PdfImageTable.ComputeTargetPixelSize`) would resample a raster bitmap back to its on-page display
  size — ~72 dpi — and silently discard the resolution. `XImage.IsRasterOutput` exempts it; do not remove that check.
