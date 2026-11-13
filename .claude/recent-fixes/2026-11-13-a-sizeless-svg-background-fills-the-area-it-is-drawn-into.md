# An SVG background with no size or viewBox is drawn into the area it is given

A standalone SVG with no `viewBox` and no `width`/`height` drew nothing as a `background-image`: the builder resolves `100%`/`50%` against
the root's viewport, which such a document does not have, so every percentage became 0. Where it stated one axis only, the other fell back
to a replaced box's 300x150, and `background-size: auto` ignored the stated axis.

- `SvgIntrinsicSize.ResolveForBackground` leaves an axis the document does not state null (the default object size of a background is its
  positioning area, CSS Images 3 §5.3); `BackgroundLayerResolver.ResolveSize` keeps a stated axis when the other is missing and there is no ratio.
- A standalone document with no `viewBox` and a missing axis gets `SvgDocument.ViewportRebuilder` (set in `ImageLoadHandler`): the painter asks
  `ForViewport` for the document built again with the tile's size as its `width`/`height` (a clone of the root with those attributes), cached per size.
  The stated size is replaced rather than kept, because a document with no viewBox has nothing to scale its content by.
- A document with a viewBox, or with both a width and a height, is untouched (its percentages and scaling already worked).
- Evidence: WPT css-backgrounds `background-size/vector/*` +64 (109 to 173), nothing lost across the 16 modules; `LayoutPaintCoverageTests` render a
  sizeless data-URI svg filling its box and at an explicit `background-size`. The `<img src>` and inline `<svg>` paths have their own sizing and were not changed.
