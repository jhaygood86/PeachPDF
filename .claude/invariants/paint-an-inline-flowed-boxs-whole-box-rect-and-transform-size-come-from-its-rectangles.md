# An inline-flowed box's whole-box rectangle and transform size come from its line rectangles

Companion to [the flowed box has no Location or Size](paint-an-inline-flowed-box-has-no-location-or-size-only-per-line-rectangles.md):
the overflow clip was the first reader of `Bounds` to go wrong, and this is the second and third.

The overflow clip was the first reader of `Bounds` to go wrong; `BoxFragment.WholeBoxRect` was the second. It is
the `transform` pivot, the `clip-path` reference box, the filter/opacity raster extent and the 3D origin, and it
read `Bounds`, so a transformed `inline-block` was pivoted around the page origin and drawn off the page. And
`Size` is *not* assigned in the sense that matters for percentages: `ActualWidth`/`ActualHeight` of a flowed box are
its padding and border only (0 wide for a default-`inline` `<img>`), so `transform-origin: 50% 50%` and
`translate(%)` landed on the corner of a content-sized badge. Both now read the line rectangle:
`FragmentEmitter.ExtentOf` and `MarginBoxContentFragmentBuilder` through `RenderUtils.ClipSourceBoundsOf`, and
`CssValueParser.ReferenceWidth`/`ReferenceHeight` for the transform's own size. **A new reader of a flowed box's
position or size must take it from `Rectangles` the same way.** The measured symptom for the transform case is a
box that is laid out (its space is reserved in the line) and painted nowhere, or rotated about the wrong point.
