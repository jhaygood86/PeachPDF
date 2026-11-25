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
- **Porter-Duff compositing and partial `clearRect`**: done as a raster fallback inside the canvas model (`CanvasBitmap.RecordComposite`), not as Core API: the drawing so far is
  rendered, combined with the new shape pixel by pixel, and kept as one image operation. The result is correct but not vector, and a canvas that composites every frame re-renders
  each time. A `PaintCompositeMode` and an erase primitive in `PeachDrawing.Core` (six backends) would keep it vector; nothing needs it yet.
- **Patterns as a fill** (`createPattern`): done by clipping to the path and drawing the tile repeatedly (not a `TileBrush`); the pattern matrix and stroke/text pattern paint are not.
- **Reading pixels**: done (see docs); `putImageData` draws over instead of replacing, and a canvas read back is replayed on a bitmap, so fonts from another backend may not show.
- **`filter`** (a CSS filter list on the context), `letterSpacing`, `wordSpacing`, `fontKerning`, `fontStretch`, `fontVariantCaps`, `textRendering`, text `lang`, wide-gamut storage (the canvas keeps sRGB; `colorSpace` converts at the `ImageData` boundary) and `alpha: false`.
- **A canvas drawn on after layout** (timer, post-layout function) is not repainted: its drawing is attached to the element when the scripts finish loading.

No GitHub issue is filed yet: file one for the compositing/raster path and one for pixel reads before building them. Not verified in this environment: the Win2D and
CoreGraphics backends (the replay uses only members they already implement).
