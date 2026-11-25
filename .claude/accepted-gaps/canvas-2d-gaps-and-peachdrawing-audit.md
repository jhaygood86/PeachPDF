# Canvas 2D: what is not built, and what PeachDrawing already covers

The canvas context (`PeachBrackets.Scripting`, `Internal/Canvas/`) records each call as an operation and replays it on any `PeachDrawing.Core.Canvas`, so PDF and SVG
get vector content. It uses **only the existing `PeachDrawing.Core` surface**; no `Canvas`/`RenderContext` member was added, so no backend (PDF, SVG, Win2D, Skia,
Raster, CoreGraphics) needed a change in this phase.

**Audit of `PeachDrawing.Core` against Canvas 2D** (Core already has):
- paths (`GraphicsPath`: move/line/Bezier/arc/close, `FillMode`), clips by path, transforms, fill and stroke by `Brush`/`Pen` (caps, joins, miter limit, dash pattern and offset);
- linear, radial (two-point, with `FocusRadius`) and conic gradients; images with source and destination rectangles and `ImageSampling`;
- layers with opacity, a CSS blend mode and effects (`DropShadowEffect`, blur, color matrix), which carry `globalAlpha`, `globalCompositeOperation` blend names and shadows;
- text drawn as strings or as glyph outlines (`GetTextOutline`), which carry `fillText` and `strokeText`.

**Gaps, each needing new `Canvas` API and an implementation in all six backends:**
- **Porter-Duff compositing** (`source-in`, `source-out`, `source-atop`, `destination-over`, `destination-in`, `destination-out`, `destination-atop`, `copy`, `xor`): `PaintBlendMode`
  has only the CSS separable and non-separable modes plus `Plus`. They draw as `source-over` today. The raster package already has `FilterOps.PorterDuff` on surfaces, so a
  raster fallback for a canvas that uses them is the likely route (a temporary surface per operation), the way `BeginRasterSurface` is used elsewhere.
- **Erasing**: `clearRect` of part of the canvas has no vector form (no "clear" primitive), so only a clear of the whole canvas is honored (it drops what was drawn).
- **Patterns as a fill** (`createPattern`): `TileBrush` exists but is made by a `Canvas` from a tile of drawing, which the recorded form does not yet build.
- **Reading pixels**: done (see docs); `putImageData` draws over instead of replacing, and a canvas read back is replayed on a bitmap, so fonts from another backend may not show.
- **`filter`** (a CSS filter list on the context), `letterSpacing`, `wordSpacing`, `fontKerning`, `fontStretch`, `fontVariantCaps`, `textRendering`, text `lang`, color spaces and `willReadFrequently`.
- **A canvas drawn on after layout** (timer, post-layout function) is not repainted: its drawing is attached to the element when the scripts finish loading.

No GitHub issue is filed yet: file one for the compositing/raster path and one for pixel reads before building them. Not verified in this environment: the Win2D and
CoreGraphics backends (the replay uses only members they already implement).
