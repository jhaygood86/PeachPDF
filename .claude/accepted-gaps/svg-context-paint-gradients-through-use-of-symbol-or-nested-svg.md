# SVG context paint: a gradient/pattern through `use` of a `symbol` or nested `svg` still measures wrong

Tracked in [#1500](https://github.com/jhaygood86/PeachPDF/issues/1500). A narrower residual of the deviations closed in
[the context-paint fix](../recent-fixes/2026-09-27-svg-context-paint-markers-use-transforms-text.md): `SvgRenderer.ContextBounds`
maps a gradient/pattern that reached a shape through `context-fill`/`context-stroke` from a `<use>` into the box of what the
`use` instantiates, in the shape's own frame - but only when `SvgGeometryBounds.GetBoundingBox` can measure the target at all.

- **`<use>` targeting a `<symbol>`** or **a nested `<svg>`**: `GetBoundingBox` has no case for either type (both establish their
  own viewBox-to-viewport mapping - the same one `SvgRenderer.RenderViewport` computes when actually painting them - rather
  than being plain geometry), so it returns null there, and `ContextBounds` falls back to the painting element's own box
  instead of the `use`'s. A gradient or pattern that reached content inside one of these targets through `context-fill`/
  `context-stroke` is measured against the wrong box whenever that differs from the target's true (viewBox-mapped) extent.
- **Solid colors are unaffected** - they resolve through the tree builder's ordinary inherited-paint mechanism
  (`InheritedPaint.ContextFill`/`ContextStroke`), which has nothing to do with bounding boxes.
- **A plain shape or `<g>` target is unaffected** (the case #1469 fixed): `GetBoundingBox` measures those directly, composing
  every descendant's own `transform` (`SvgGeometryBounds.UnionAll`), and `SvgRenderer`'s `s_paintContextFrames` records the
  `use`'s own frame for that switch arm.

Why left: a correct fix means extending `GetBoundingBox` to replicate `RenderViewport`'s own viewBox-to-viewport mapping in a
measurement-only pass (for both a `<symbol>` and a nested `<svg>`), then recording the `use`'s context frame for those two
`RenderElementSwitch` arms too (today only the plain-element arm does, since it is the only target type `GetBoundingBox`
already measures) - a materially larger, separately-scoped change than the three coordinate-space fixes #1469 closed, which
never touched symbol/nested-svg content at all. What was measured: `SvgContextPaintTests` pins the plain-shape/group-through-
`use` case (with and without an intervening `transform`); no test exercises a symbol/nested-svg target, since none is
expected to pass yet.
