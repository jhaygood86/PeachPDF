# Canvas 2D as a recording replayed on any Canvas

**Idea.** A `CanvasBitmap` keeps each drawing call as an `Action<Canvas>` with the state it was made with (clips as path data, transform, alpha, blend, shadow); the replay
pushes the clips, opens a layer when alpha/blend/shadow ask for one, pushes the transform and draws. The engine seam is `ScriptCanvasPainting(Width, Height, Paint)` set with
`ScriptDocument.SetCanvasPainting`; at the end of the script run it is keyed by the element's source ordinal (`PublishCanvasPaintings`) into `HtmlContainerInt.CanvasPaintings`,
and `CssBoxCanvas` (a replaced box modeled on `CssBoxFrame`) + `CanvasFragmentPainter` replay it scaled to the box. Paths are stored in device space as the spec says, and mapped
back through the inverse of the current matrix at fill/stroke so gradients and pen widths live in user space under `PushTransform`.

**Traps found by running it.**
- The page's 20 pt screen margin and an inline replaced box's baseline gap put the canvas at (20, 24) in page space; the first "nothing is painted" was the test's coordinates (use `SetMargins(0)` and `display:block`).
- A new replaced box type has to be added to the lists that name `CssBoxImage`/`CssBoxFrame` (monolithic content, structure tags, three spots in `CssLayoutEngine`, `display: contents` handling), or it lays out as a generic box.
- `GraphicsPath` objects belong to the `Canvas` that made them, so the recording keeps its own `PathData` and builds a path at replay time.

**Verified.** Rendered through the CLI and rasterized with MuPDF and PDFium: the gradient, translucent circle, dashed round-capped curve, drop shadow and text all appear, as vector content.
**Not done.** See [the gap file](../accepted-gaps/canvas-2d-gaps-and-peachdrawing-audit.md). No WPT canvas floors (`html/canvas` is not in the fetched checkout; adding it is a larger download).
