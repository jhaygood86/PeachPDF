# PeachDrawing.Skia LCD text: two coefficient blends, only over an opaque backdrop

`SkiaCanvas` draws LCD text as `Modulate` (keep = 255 - coverage per channel) then `Plus` (ink x coverage per channel) of two opaque images placed on whole device pixels
under an identity matrix (`SkiaCanvas.TextMask.cs`). That is the per-channel blend `dst*(1-a)+ink*a` and it needs no readback, so it works on a CPU raster canvas and a
GPU lease alike. What a future change must not break:

- **Only coefficient blend modes.** A runtime-effect blender or a readback would work on one canvas kind and not the other (a leased GPU canvas cannot be read).
- **Only over an opaque backdrop.** Per-channel coverage over a pixel with alpha has no meaning (the fringes would be baked into transparency). The host says so with
  `SkiaCanvas.OpaqueBackdrop`; a layer's content canvas is never opaque, a recorded tile and a blend mode never take the mask path. A view must set it from the color it clears
  its surface to, never unconditionally.
- **`Auto` consults the system only for `Auto`.** An explicit `Grayscale`/`Outline` must ignore a "subpixel" system answer (a test with a fake `ISystemTextSettings` pins it).
- **The fitted constants are shared with Win2D** (`SkiaTextBlend` / `Win2dTextBlend`): change both or neither.
- **Nothing on the render thread may read an `AvaloniaObject`**; the view's `TextRendering` travels in `PaintState`.
