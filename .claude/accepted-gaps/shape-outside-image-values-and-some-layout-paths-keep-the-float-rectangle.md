# `shape-outside` is applied in the ordinary line flow only

`shape-outside` supports basic shapes, `path()`, a shape-box and a raster `url()` image with `shape-image-threshold`, plus `shape-margin`. Not done, tracked in issue #2173:

- `CssLayoutEngine.ExtentBesideFloats` (a multi-column container narrowed beside a float), the inline-float shift helper (`ShiftLineBesideFloat`) and the crowded-line shift (`ShiftEmptyLineBelowCrowdingFloats`) still read the float's margin rectangle. The shape is applied in `FlowBox`'s per-word wrap (`FloatLeftEdge`/`FloatRightEdge`) and the atomic-inline fit.
- Vertical writing modes use the float's rectangle (their float path is separate, see [the two entry points](../invariants/vertical-a-float-has-two-entry-points.md)).
- An SVG image or a gradient as the shape image is not read (the float keeps its rectangle); only raster images are.
- When the nearest float's shape does not reach a line, the line starts at its own content edge rather than beside an earlier left float stacked next to the shaped one.
