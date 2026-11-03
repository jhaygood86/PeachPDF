# `shape-outside` supports basic shapes only, and only on the ordinary line flow

`shape-outside` takes `circle()`, `ellipse()`, `inset()`, `polygon()` and a `<shape-box>`, plus `shape-margin`. Not done, tracked in issue #2156:

- An `<image>` value (shape from the image's alpha above `shape-image-threshold`) and `path()`/`url()` are dropped at parse time, so the float keeps its rectangle. Layout does not read an image's pixels.
- `CssLayoutEngine.ExtentBesideFloats` (a multi-column container narrowed beside a float) and the inline-float shift helper (`ShiftLineBesideFloat`) and the crowded-line shift (`ShiftEmptyLineBelowCrowdingFloats`) still read the float's margin rectangle. The shape is applied in `FlowBox`'s per-word wrap (`FloatLeftEdge`/`FloatRightEdge`) and the atomic-inline fit.
- Vertical writing modes use the float's rectangle (their float path is separate, see [the two entry points](../invariants/vertical-a-float-has-two-entry-points.md)).
- When the shape of the nearest float does not reach a line, the line starts at its own content edge rather than beside an earlier left float that is stacked next to the shaped one.
