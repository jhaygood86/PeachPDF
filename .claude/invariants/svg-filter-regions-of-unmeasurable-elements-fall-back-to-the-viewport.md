# An SVG filter's objectBoundingBox region needs a fallback for elements with no static bounds

`SvgGeometryBounds.GetBoundingBox` cannot measure text (a glyph run has no geometry until it is laid out with a font), so a `<g>` or
`<text>` with `filter="url(#f)"` has no bounding box. An `objectBoundingBox` filter region (the default, `-10%/-10%/120%/120%`) resolved
against "no box" fell back to the raw fractions, a 1.2 x 1.2 user-unit square at the origin: the element vanished (in the vector evaluation
too, where it had always been a silent bug).

**Rule:** `SvgFilterEvaluator.ElementBounds` returns the viewport when the box cannot be measured; both evaluations use it. The old
overload without a viewport keeps the previous behaviour for the unit tests that pin the evaluator's graph resolution.
