# GPU offload on Win2D and Skia/Android

**What landed.** A new `Canvas.HasNativeLayerEffects` (true for `Win2dCanvas` and `SkiaCanvas`) lets the HTML painter draw a blurred outset
`box-shadow` (`FragmentPainter.RasterShadows.cs`), a CSS `filter` (`FragmentPainter.Raster.cs`, `RasterFilterChain.ToLayerEffects`) and
`backdrop-filter` (`FragmentPainter.Backdrop.cs`) as a layer with effects instead of through a CPU bitmap; every other canvas keeps the bitmap
path. Win2D also got a conic-gradient pixel shader, a pooled copy in `DrawRaster`, and `Auto` image codecs (Win2D and Skia) that keep the
encoded bytes and let the platform decode onto the device. Android `HtmlView` draws on an `SKGLSurfaceView` and keeps the `Content` layer as
1024 px GPU tiles (`HtmlView.Painting.cs`, `Logic/TileGrid.cs`), painting `ViewportAttached` over them each frame.

**Traps found by running it, not reading it.**

- **A passing test proved nothing three times.** The conic shader first threw `ArgumentException` and silently fell back to the wedges, so the
  parity test compared wedges with wedges. Every new native path was checked by making it throw (a probe) and watching the tests fail; do that
  for any "native path with a fallback".
- **Win2D `PixelShaderEffect` constants are one property per cbuffer variable** (`Properties["center"]`, `["colors"]`...), not a `"constants"`
  byte blob. Setting the blob is `E_INVALIDARG`.
- **Win2D `CanvasBitmap.LoadAsync` applies the EXIF orientation and cannot be told not to.** The rest of PeachDrawing applies it from
  `ExifOrientation`, so a GPU-resident image with an orientation would rotate twice (found as an 8-wide bitmap for a 16-wide image). Such images
  go to PeachImage in `Auto`. Skia's `SKCodec.EncodedOrigin` gives the same gate.
- **`GaussianBlurEffect` `BorderMode.Hard` clamps the source's edge and stops a blur spreading past its content** (every shadow lost its
  falloff), `Soft` blurs into transparency (a backdrop darkens at the panel's edge). Hence the opt-in `BlurEffect.ClampEdges`, used only by
  backdrop-filter; Skia maps it to `SKShaderTileMode.Clamp` against the default `Decal`.
- **The 360-wedge conic gradient doubled the alpha of a translucent stop** along every seam (a 128-alpha stop read 192): the wedges overlap by
  5%. The shader gives the exact alpha, which is why the parity test uses opaque stops.
- **An effect drawn under a transform is positioned in local space**; the shader centre is the local centre times `Dpi / 96`, not transformed.
- **SkiaSharp 3.x names the GL view `SKGLSurfaceView`** (there is no `SKGLView`); drive it with `RequestRender()` and `RenderMode.WhenDirty`.
- **The Android example ANRs on startup on a slow emulator in CPU mode too** (the document loads and lays out on the UI thread for ~8 s); it is
  not caused by the GL surface. Emulator GL is software (swiftshader), so its frame times say nothing about a real GPU.

**Not done, deliberately.** Reusing a `CanvasBitmap` in `DrawRaster` (a recorded command list keeps a reference to the bitmap, so overwriting its
pixels would change earlier draws); caching the Skia `DrawRaster` image (the raster surface is disposed right after the draw); a Win2D tile
cache and GPU text rasterisation; a Skia glyph atlas. No pixel-for-pixel comparison against the old CPU paths or showcase rasterisation was run.
