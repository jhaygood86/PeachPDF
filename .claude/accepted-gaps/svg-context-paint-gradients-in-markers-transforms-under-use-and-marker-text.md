# SVG `context-fill` / `context-stroke`: gradients in markers, transforms under `use`, text in markers

Tracked in [#1469](https://github.com/jhaygood86/PeachPDF/issues/1469). The keywords resolve for solid colours and `none` everywhere the
spec defines a context element (`SvgTreeBuilder.ResolveContextPaint` for a `use` and the seed a build is given, `SvgRenderer.ResolveInMarker` for a
marker). Three deviations remain:

- **A gradient or pattern on the shape a marker is drawn on** is not carried into the marker (`SvgRenderer.ForMarker`): the paint server must keep
  the context element's coordinate space and box, and the marker's placement matrix has moved the content away from both. Doing it means mapping
  the box through the inverse of the placement, per instance.
- **A gradient or pattern through `use`** is measured against the box of what the `use` instantiates (`SvgRenderer.ContextBounds`), in that element's
  space. A `transform` between the `use`'s target and the painting element is not undone, so the box is only right when there is none.
- **Text in a marker** whose `fill`/`stroke` is a context keyword: the text painters read `run.Fill`/`run.Stroke` directly, not through
  `ResolveInMarker`, so the keyword is treated as no paint. Two side effects of that: text with such a stroke takes the outlined path (its stroke
  counts as a stroke), and a text decoration falls back to black. A shape in a marker that reads `FillPaint`/`StrokePaint` as a filter input
  gets a blank one for the same reason (`RendererFilterInputs.PaintOf`).

Why left: none affects an OpenType SVG glyph (the documents this was written for use `use` with solid paint), and each needs a coordinate-space
rather than a colour substitution. What was measured: the marker and `use` colour cases, and the gradient-through-`use` box, each pinned in
`SvgContextPaintTests`.
